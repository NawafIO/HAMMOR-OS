using HAMMOR.Core.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HAMMOR.App.Shell;

/// <summary>
/// Counts the tasks that are Blocked, waiting for the user's approval, so the
/// sidebar can say so without the Tasks page being open.
/// </summary>
/// <remarks>
/// <para>
/// Read-only: it lists Blocked tasks once and then follows
/// <see cref="ITaskStore.TaskChanged"/>, exactly as the Living Core's presenter
/// does for its own Blocked state. It changes nothing in the store.
/// </para>
/// <para>
/// <see cref="ITaskStore.TaskChanged"/> can be raised on any thread, so
/// <see cref="CountChanged"/> is too; the shell marshals it to its dispatcher.
/// A change seen while the first listing is still running wins over that
/// listing, so a task that was resumed in the meantime is not counted again.
/// </para>
/// </remarks>
public sealed class BlockedTaskTracker : IDisposable
{
    private readonly ITaskStore _store;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly HashSet<string> _blocked = new(StringComparer.Ordinal);
    private readonly HashSet<string> _changedWhileLoading = new(StringComparer.Ordinal);

    private bool _loading;
    private bool _started;
    private bool _disposed;

    public BlockedTaskTracker(ITaskStore store, ILogger<BlockedTaskTracker>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? NullLogger<BlockedTaskTracker>.Instance;
    }

    /// <summary>Raised, on the calling thread, when the number of Blocked tasks changes.</summary>
    public event EventHandler? CountChanged;

    /// <summary>How many tasks are Blocked right now.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _blocked.Count;
            }
        }
    }

    /// <summary>
    /// Starts following the store and reads the tasks that are already Blocked.
    /// A store that cannot be read is logged and leaves the count at what the
    /// events report; it never throws.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
            _loading = true;
        }

        _store.TaskChanged += OnTaskChanged;

        IReadOnlyList<HammorTask> listed;
        try
        {
            listed = await _store.ListAsync([TaskState.Blocked], cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            FinishLoading(null);
            throw;
        }
        catch (Exception ex)
        {
            // A sidebar hint must not stop the shell; the Tasks page still
            // shows every Blocked task.
            _logger.LogWarning(ex, "Blocked tasks could not be listed for the sidebar.");
            FinishLoading(null);
            return;
        }

        FinishLoading(listed);
    }

    /// <summary>Folds one task change into the count.</summary>
    internal void Apply(HammorTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        bool changed;
        lock (_gate)
        {
            if (_loading)
            {
                _changedWhileLoading.Add(task.Id);
            }

            changed = task.State == TaskState.Blocked
                ? _blocked.Add(task.Id)
                : _blocked.Remove(task.Id);
        }

        if (changed)
        {
            CountChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _store.TaskChanged -= OnTaskChanged;
    }

    private void OnTaskChanged(object? sender, HammorTask task) => Apply(task);

    private void FinishLoading(IReadOnlyList<HammorTask>? listed)
    {
        bool changed = false;
        lock (_gate)
        {
            if (listed is not null)
            {
                foreach (var task in listed)
                {
                    if (task.State == TaskState.Blocked
                        && !_changedWhileLoading.Contains(task.Id)
                        && _blocked.Add(task.Id))
                    {
                        changed = true;
                    }
                }
            }

            _loading = false;
            _changedWhileLoading.Clear();
        }

        if (changed)
        {
            CountChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
