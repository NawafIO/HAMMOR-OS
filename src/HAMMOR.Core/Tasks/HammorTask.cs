namespace HAMMOR.Core.Tasks;

/// <summary>Lifecycle states a task can occupy.</summary>
public enum TaskState
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,

    /// <summary>
    /// Waiting for the user: an unattended run needed authority it was not
    /// granted. Not terminal. Returns to Pending only through
    /// <see cref="ITaskStore.ResumeBlockedAsync"/> with a new grant.
    /// </summary>
    Blocked = 5,
}

/// <summary>A unit of work HAMMOR tracks across restarts.</summary>
public sealed record HammorTask
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");

    public required string Title { get; init; }

    public string? Description { get; init; }

    public TaskState State { get; init; } = TaskState.Pending;

    public string? ProjectId { get; init; }

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedUtc { get; init; }

    public DateTimeOffset? CompletedUtc { get; init; }

    /// <summary>
    /// Earliest time this task should run. Null means "as soon as possible".
    /// The scheduler in a later phase reads this field.
    /// </summary>
    public DateTimeOffset? ScheduledForUtc { get; init; }

    /// <summary>Attempts made so far, including the current one.</summary>
    public int AttemptCount { get; init; }

    public int MaxAttempts { get; init; } = 1;

    /// <summary>
    /// Upper bound on <see cref="MaxAttempts"/> for unattended execution.
    /// Enforced when a granted task is authored or stored, and the runner
    /// never retries beyond it whatever a stored row says.
    /// </summary>
    public const int MaxAttemptsLimit = 10;

    /// <summary>Failure detail when <see cref="State"/> is Failed.</summary>
    public string? Error { get; init; }

    /// <summary>Human-readable result when the task completed.</summary>
    public string? Result { get; init; }

    /// <summary>
    /// Instruction handed to the agent when the task runs unattended. Distinct
    /// from <see cref="Title"/>/<see cref="Description"/>, which are display text.
    /// </summary>
    public string? Prompt { get; init; }

    /// <summary>Why the task is Blocked, when <see cref="State"/> is Blocked.</summary>
    public string? BlockedReason { get; init; }

    /// <summary>
    /// Authority for unattended execution. Null means the task is
    /// interactive-only and the runner will never execute it. Immutable once
    /// stored: replaced only by <see cref="ITaskStore.ResumeBlockedAsync"/>.
    /// </summary>
    public TaskGrant? Grant { get; init; }

    public bool IsTerminal =>
        State is TaskState.Completed or TaskState.Failed or TaskState.Cancelled;
}
