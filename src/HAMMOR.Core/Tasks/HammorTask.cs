namespace HAMMOR.Core.Tasks;

/// <summary>Lifecycle states a task can occupy.</summary>
public enum TaskState
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
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

    /// <summary>Failure detail when <see cref="State"/> is Failed.</summary>
    public string? Error { get; init; }

    /// <summary>Human-readable result when the task completed.</summary>
    public string? Result { get; init; }

    public bool IsTerminal =>
        State is TaskState.Completed or TaskState.Failed or TaskState.Cancelled;
}
