namespace HAMMOR.Core.Memory;

/// <summary>
/// Optional vector/semantic search over memory, kept as an abstraction so an
/// embedding backend can be added later without changing callers.
/// </summary>
/// <remarks>
/// Phase 1 ships no implementation. <see cref="IsAvailable"/> is false and
/// callers fall back to <see cref="IMemoryStore.SearchAsync"/> keyword search;
/// the UI must present semantic search as unavailable rather than silently
/// degrading.
/// </remarks>
public interface ISemanticMemoryIndex
{
    /// <summary>
    /// Whether a working embedding backend is wired up. False in Phase 1.
    /// </summary>
    bool IsAvailable { get; }

    Task IndexAsync(MemoryEntry entry, CancellationToken cancellationToken = default);

    Task RemoveAsync(string entryId, CancellationToken cancellationToken = default);

    /// <summary>Nearest entries by embedding similarity, best first.</summary>
    Task<IReadOnlyList<SemanticMatch>> SearchAsync(
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default);
}

/// <param name="EntryId">Matching <see cref="MemoryEntry.Id"/>.</param>
/// <param name="Score">Similarity, higher is closer.</param>
public sealed record SemanticMatch(string EntryId, double Score);
