using HAMMOR.Core.Memory;

namespace HAMMOR.Infrastructure.Memory;

/// <summary>
/// Placeholder semantic index. NOT IMPLEMENTED — no embeddings are computed.
/// </summary>
/// <remarks>
/// Satisfies the <see cref="ISemanticMemoryIndex"/> dependency so the
/// abstraction is in place for a future embedding backend.
/// <see cref="IsAvailable"/> is false, and search returns an empty list rather
/// than silently degrading to keyword matching — callers that want keyword
/// search must ask <see cref="IMemoryStore.SearchAsync"/> for it explicitly.
/// </remarks>
public sealed class UnavailableSemanticMemoryIndex : ISemanticMemoryIndex
{
    public bool IsAvailable => false;

    public Task IndexAsync(MemoryEntry entry, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RemoveAsync(string entryId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<SemanticMatch>> SearchAsync(
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SemanticMatch>>([]);
}
