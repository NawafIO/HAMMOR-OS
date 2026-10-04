namespace HAMMOR.Core.Projects;

/// <summary>Persistence for <see cref="HammorProject"/>.</summary>
public interface IProjectStore
{
    Task<HammorProject> CreateAsync(
        HammorProject project,
        CancellationToken cancellationToken = default);

    Task<HammorProject?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<HammorProject> UpdateAsync(
        HammorProject project,
        CancellationToken cancellationToken = default);

    /// <summary>Projects ordered by most recently opened.</summary>
    Task<IReadOnlyList<HammorProject>> ListAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
