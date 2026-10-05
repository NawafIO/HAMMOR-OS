using HAMMOR.Core.Agent;
using HAMMOR.Core.Audit;
using HAMMOR.Core.Diagnostics;
using HAMMOR.Core.Tools;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Core.Tasks;

public enum TaskRunStatus
{
    /// <summary>Nothing was due.</summary>
    NoWork = 0,

    /// <summary>Another task is already running; v1 runs exactly one at a time.</summary>
    Busy = 1,

    Completed = 2,

    Failed = 3,

    Blocked = 4,

    Cancelled = 5,

    /// <summary>The run failed and was returned to Pending for another attempt.</summary>
    Retrying = 6,
}

/// <param name="TaskId">Task acted on, when there was one.</param>
/// <param name="Status">What happened.</param>
/// <param name="Message">Short redacted description.</param>
public sealed record TaskRunReport(string? TaskId, TaskRunStatus Status, string Message);

/// <summary>Executes due tasks unattended, one at a time (ADR-003).</summary>
public interface ITaskRunner
{
    /// <summary>
    /// Blocks tasks whose grant is no longer valid, then runs at most one due
    /// task to completion. Returns <see cref="TaskRunStatus.Busy"/> without
    /// doing anything when a run is already in progress.
    /// </summary>
    Task<TaskRunReport> RunNextAsync(CancellationToken cancellationToken = default);

    /// <summary>Cancels the task if it is the one currently running.</summary>
    bool RequestCancel(string taskId);
}

/// <summary>
/// Default runner. Every tool call goes through <see cref="IAgentPipeline"/>
/// (and so <see cref="AgentLoop"/>); the runner adds only task lifecycle,
/// grant checks, audit and cancellation. It never invokes a tool directly.
/// </summary>
public sealed class TaskRunner(
    ITaskStore taskStore,
    IAgentPipeline pipeline,
    IToolRegistry toolRegistry,
    IAuditLog auditLog,
    ILogger<TaskRunner> logger,
    TimeProvider? timeProvider = null) : ITaskRunner
{
    private const int MaxResultChars = 4_000;
    private const int MaxReasonChars = 1_000;
    private const int MaxBackoffSeconds = 900;

    private readonly ITaskStore _store = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
    private readonly IAgentPipeline _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    private readonly IToolRegistry _registry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
    private readonly IAuditLog _audit = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
    private readonly ILogger<TaskRunner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    // The single execution slot: one task at a time.
    private readonly SemaphoreSlim _slot = new(1, 1);
    private readonly object _runningLock = new();
    private string? _runningTaskId;
    private CancellationTokenSource? _runningCts;

    public bool RequestCancel(string taskId)
    {
        lock (_runningLock)
        {
            if (_runningCts is null || !string.Equals(_runningTaskId, taskId, StringComparison.Ordinal))
            {
                return false;
            }

            _runningCts.Cancel();
            return true;
        }
    }

    public async Task<TaskRunReport> RunNextAsync(CancellationToken cancellationToken = default)
    {
        if (!await _slot.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new TaskRunReport(null, TaskRunStatus.Busy, "A task is already running.");
        }

        try
        {
            var now = _time.GetUtcNow();
            var pending = await _store.ListAsync([TaskState.Pending], null, 1000, cancellationToken)
                .ConfigureAwait(false);

            // Tasks without a grant are interactive-only: never executed here.
            var due = pending
                .Where(t => t.Grant is not null && DueAt(t) <= now)
                .OrderBy(DueAt)
                .ThenBy(t => t.CreatedUtc)
                .ToList();

            foreach (var task in due)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var problem = FindProblem(task, now);
                if (problem is not null)
                {
                    await BlockAsync(task, problem, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return await ExecuteAsync(task, cancellationToken).ConfigureAwait(false);
            }

            return new TaskRunReport(null, TaskRunStatus.NoWork, "No task is due.");
        }
        finally
        {
            _slot.Release();
        }
    }

    private static DateTimeOffset DueAt(HammorTask task) => task.ScheduledForUtc ?? task.CreatedUtc;

    /// <summary>Why a due task must not run right now, or null when it may.</summary>
    private string? FindProblem(HammorTask task, DateTimeOffset now)
    {
        var grant = task.Grant!;

        if (grant.SupersededUtc is not null)
        {
            return "The task's grant has been superseded.";
        }

        if (!string.Equals(grant.TaskId, task.Id, StringComparison.Ordinal))
        {
            return "The task's grant belongs to a different task.";
        }

        if (grant.ProjectId is not null
            && !string.Equals(grant.ProjectId, task.ProjectId, StringComparison.Ordinal))
        {
            return "The task's grant is scoped to a different project.";
        }

        var errors = TaskGrantValidator.Validate(grant, _registry, now);
        if (errors.Count > 0)
        {
            return "The task's grant is not valid: " + string.Join(" ", errors);
        }

        if (string.IsNullOrWhiteSpace(task.Prompt))
        {
            return "The task has no prompt to run.";
        }

        return null;
    }

    private async Task BlockAsync(HammorTask task, string reason, CancellationToken cancellationToken)
    {
        var safe = Bound(reason, MaxReasonChars);

        // Audit first: if the audit write fails the task stays Pending and is
        // simply not run, which is the safe direction.
        await AuditAsync(task, AuditOutcome.Denied, $"Task blocked before running: {safe}", cancellationToken)
            .ConfigureAwait(false);

        await _store.UpdateAsync(task with { State = TaskState.Blocked, BlockedReason = safe }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TaskRunReport> ExecuteAsync(HammorTask task, CancellationToken cancellationToken)
    {
        var grant = task.Grant!;
        var attempt = task.AttemptCount + 1;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_runningLock)
        {
            _runningTaskId = task.Id;
            _runningCts = cts;
        }

        try
        {
            await AuditAsync(
                task,
                AuditOutcome.Information,
                $"Run started (attempt {attempt}/{task.MaxAttempts}); grant {grant.Id}: tools [{string.Join(", ", grant.AllowedTools)}], "
                + $"max {grant.MaxToolCalls} call(s), expires {grant.ExpiresUtc:O}.",
                CancellationToken.None).ConfigureAwait(false);

            var running = await _store.UpdateAsync(
                task with
                {
                    State = TaskState.Running,
                    StartedUtc = _time.GetUtcNow(),
                    AttemptCount = attempt,
                    BlockedReason = null,
                    Error = null,
                },
                CancellationToken.None).ConfigureAwait(false);

            var context = new UnattendedRunContext(task.Id, grant, _time);
            AgentTurnResult result;

            try
            {
                result = await _pipeline.RunUnattendedAsync(
                    new AgentTurnRequest { Input = task.Prompt!, ProjectId = task.ProjectId },
                    context,
                    progress: null,
                    cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return await FinishAsync(
                    running,
                    TaskState.Cancelled,
                    TaskRunStatus.Cancelled,
                    "Run cancelled.",
                    error: null,
                    resultText: null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Includes a failed audit write inside the loop: that fails the run.
                _logger.LogError(ex, "Unattended run of task {TaskId} faulted.", task.Id);
                result = AgentTurnResult.Failed(AgentStage.Execute, $"Run faulted: {ex.Message}");
            }

            if (result.Succeeded)
            {
                return await FinishAsync(
                    running,
                    TaskState.Completed,
                    TaskRunStatus.Completed,
                    "Run completed.",
                    error: null,
                    resultText: Bound(result.ReplyText, MaxResultChars)).ConfigureAwait(false);
            }

            if (result.IsBlocked)
            {
                var reason = Bound(result.BlockedReason ?? "Blocked.", MaxReasonChars);
                await AuditAsync(running, AuditOutcome.Denied, $"Run blocked: {reason}", CancellationToken.None)
                    .ConfigureAwait(false);

                // A block is not a failure: it does not consume a retry attempt.
                await _store.UpdateAsync(
                    running with
                    {
                        State = TaskState.Blocked,
                        BlockedReason = reason,
                        AttemptCount = Math.Max(0, running.AttemptCount - 1),
                    },
                    CancellationToken.None).ConfigureAwait(false);

                return new TaskRunReport(task.Id, TaskRunStatus.Blocked, reason);
            }

            var error = Bound(result.Error ?? "The run failed.", MaxReasonChars);

            if (attempt < task.MaxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30d * Math.Pow(2, attempt - 1), MaxBackoffSeconds));
                var next = _time.GetUtcNow() + delay;

                await AuditAsync(
                    running,
                    AuditOutcome.Failed,
                    $"Run failed (attempt {attempt}/{task.MaxAttempts}): {error} Retry scheduled for {next:O}.",
                    CancellationToken.None).ConfigureAwait(false);

                await _store.UpdateAsync(
                    running with { State = TaskState.Pending, ScheduledForUtc = next, Error = error },
                    CancellationToken.None).ConfigureAwait(false);

                return new TaskRunReport(task.Id, TaskRunStatus.Retrying, error);
            }

            return await FinishAsync(
                running,
                TaskState.Failed,
                TaskRunStatus.Failed,
                $"Run failed: {error}",
                error,
                resultText: null).ConfigureAwait(false);
        }
        finally
        {
            lock (_runningLock)
            {
                _runningTaskId = null;
                _runningCts = null;
            }
        }
    }

    /// <summary>Writes the terminal state and its audit entry. Not cancellable: cancel must still be recorded.</summary>
    private async Task<TaskRunReport> FinishAsync(
        HammorTask running,
        TaskState state,
        TaskRunStatus status,
        string message,
        string? error,
        string? resultText)
    {
        await AuditAsync(
            running,
            state == TaskState.Completed ? AuditOutcome.Succeeded
                : state == TaskState.Cancelled ? AuditOutcome.Information
                : AuditOutcome.Failed,
            message,
            CancellationToken.None).ConfigureAwait(false);

        await _store.UpdateAsync(
            running with
            {
                State = state,
                CompletedUtc = _time.GetUtcNow(),
                Error = error,
                Result = resultText,
            },
            CancellationToken.None).ConfigureAwait(false);

        return new TaskRunReport(running.Id, status, message);
    }

    private Task AuditAsync(
        HammorTask task,
        AuditOutcome outcome,
        string message,
        CancellationToken cancellationToken) =>
        _audit.AppendAsync(
            new AuditEntry
            {
                Category = AuditCategory.TaskLifecycle,
                Subject = $"task:{task.Id}",
                Message = SecretRedactor.Redact(message),
                Outcome = outcome,
                ProjectId = task.ProjectId,
                CorrelationId = task.Id,
            },
            cancellationToken);

    private static string Bound(string value, int maxChars)
    {
        var redacted = SecretRedactor.Redact(value ?? string.Empty);
        return redacted.Length <= maxChars
            ? redacted
            : redacted[..maxChars] + "… [truncated]";
    }
}
