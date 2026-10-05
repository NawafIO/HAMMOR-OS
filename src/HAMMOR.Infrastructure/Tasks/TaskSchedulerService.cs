using HAMMOR.Core.Tasks;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Infrastructure.Tasks;

/// <summary>
/// Drives <see cref="ITaskRunner"/> in the background (ADR-003). No polling
/// loop: it wakes when the task store reports a change, and otherwise sleeps
/// until the next scheduled task is due, bounded at <see cref="MaxIdle"/>.
/// Execution stays one task at a time because the runner owns a single slot.
/// Nothing starts until <see cref="Start"/> is called by the host.
/// </summary>
public sealed class TaskSchedulerService(
    ITaskRunner runner,
    ITaskStore taskStore,
    ILogger<TaskSchedulerService> logger,
    TimeProvider? timeProvider = null) : IAsyncDisposable
{
    internal static readonly TimeSpan MaxIdle = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

    private readonly ITaskRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    private readonly ITaskStore _store = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
    private readonly ILogger<TaskSchedulerService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Starts the background loop. Idempotent.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_loop is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _store.TaskChanged += OnTaskChanged;
            var token = _cts.Token;
            _loop = Task.Run(() => LoopAsync(token));
        }
    }

    /// <summary>Stops the loop and cancels any run in progress.</summary>
    public async Task StopAsync()
    {
        Task? loop;
        CancellationTokenSource? cts;

        lock (_gate)
        {
            loop = _loop;
            cts = _cts;
            _loop = null;
            _cts = null;
            _store.TaskChanged -= OnTaskChanged;
        }

        if (loop is null || cts is null)
        {
            return;
        }

        cts.Cancel();

        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            cts.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _wake.Dispose();
    }

    private void OnTaskChanged(object? sender, HammorTask task)
    {
        try
        {
            _wake.Release();
        }
        catch (ObjectDisposedException)
        {
            // Stopped while an event was in flight.
        }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = MaxIdle;

            try
            {
                TaskRunReport report;
                do
                {
                    report = await _runner.RunNextAsync(cancellationToken).ConfigureAwait(false);
                }
                while (report.Status is not (TaskRunStatus.NoWork or TaskRunStatus.Busy));

                delay = await ComputeDelayAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Task scheduler pass failed; retrying later.");
                delay = ErrorDelay;
            }

            try
            {
                await _wake.WaitAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Collapse a burst of change events into one pass.
            while (_wake.Wait(0))
            {
            }
        }
    }

    private async Task<TimeSpan> ComputeDelayAsync(CancellationToken cancellationToken)
    {
        var pending = await _store.ListAsync([TaskState.Pending], null, 1000, cancellationToken)
            .ConfigureAwait(false);

        var now = _time.GetUtcNow();
        var due = pending
            .Where(t => t.Grant is not null)
            .Select(t => t.ScheduledForUtc ?? t.CreatedUtc)
            .ToList();

        if (due.Count == 0)
        {
            return MaxIdle;
        }

        var wait = due.Min() - now;
        return wait < MinDelay ? MinDelay : wait > MaxIdle ? MaxIdle : wait;
    }
}
