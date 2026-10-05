namespace HAMMOR.Core.Tasks;

/// <summary>
/// Legal task state transitions (ADR-003). Enforced by the store, so neither
/// the runner nor the UI can skip them.
/// </summary>
public static class TaskStateMachine
{
    /// <remarks>
    /// Blocked → Pending is deliberately absent: it is only possible through
    /// <see cref="ITaskStore.ResumeBlockedAsync"/> with a new grant. Blocked →
    /// Running is never legal. Terminal states have no exits.
    /// </remarks>
    public static bool IsAllowed(TaskState from, TaskState to)
    {
        if (from == to)
        {
            return true;
        }

        return from switch
        {
            TaskState.Pending => to is TaskState.Running
                or TaskState.Completed
                or TaskState.Failed
                or TaskState.Cancelled
                or TaskState.Blocked,
            TaskState.Running => to is TaskState.Completed
                or TaskState.Failed
                or TaskState.Cancelled
                or TaskState.Blocked
                or TaskState.Pending,
            TaskState.Blocked => to is TaskState.Cancelled or TaskState.Failed,
            _ => false,
        };
    }
}
