namespace HAMMOR.Core.Tasks;

/// <summary>
/// Persistence for <see cref="HammorTask"/>. Durable so that a task left
/// Running by a crash can be detected and reconciled on the next launch.
/// </summary>
public interface ITaskStore
{
    Task<HammorTask> CreateAsync(HammorTask task, CancellationToken cancellationToken = default);

    Task<HammorTask?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<HammorTask> UpdateAsync(HammorTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tasks in the given states, newest first. Pass null for all states.
    /// </summary>
    Task<IReadOnlyList<HammorTask>> ListAsync(
        IReadOnlyCollection<TaskState>? states = null,
        string? projectId = null,
        int limit = 200,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks tasks still recorded as Running as Failed on startup. A Running
    /// row at launch means the process died mid-task; leaving it Running would
    /// make the UI claim work is in progress when nothing is executing.
    /// </summary>
    Task<int> ReconcileInterruptedAsync(CancellationToken cancellationToken = default);

    event EventHandler<HammorTask>? TaskChanged;
}
