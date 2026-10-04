using HAMMOR.Core.Memory;

namespace HAMMOR.Infrastructure.Memory;

/// <summary>
/// Decorates a memory store so every save also lands in the markdown mirror,
/// and records the content hash used for incremental indexing.
/// </summary>
/// <remarks>
/// Composed rather than merged into <see cref="Persistence.SqliteMemoryStore"/>
/// so that SQLite persistence stays independently testable and the mirror can
/// be swapped out (or disabled) without touching query code.
/// </remarks>
public sealed class MarkdownMirroringMemoryStore(
    IMemoryStore inner,
    MarkdownMemoryMirror mirror) : IMemoryStore
{
    private readonly IMemoryStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    private readonly MarkdownMemoryMirror _mirror =
        mirror ?? throw new ArgumentNullException(nameof(mirror));

    public async Task<MemoryEntry> SaveAsync(
        MemoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var hash = MarkdownMemoryMirror.ComputeHash(entry.Content);
        var relativePath = await _mirror.WriteAsync(entry, cancellationToken).ConfigureAwait(false);

        var enriched = entry with
        {
            ContentHash = hash,
            MarkdownPath = relativePath ?? entry.MarkdownPath,
        };

        return await _inner.SaveAsync(enriched, cancellationToken).ConfigureAwait(false);
    }

    public Task<MemoryEntry?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        _inner.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<MemoryEntry>> SearchAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default) =>
        _inner.SearchAsync(query, limit, cancellationToken);

    public Task<IReadOnlyList<MemoryEntry>> GetRecentAsync(
        string? projectId = null,
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        _inner.GetRecentAsync(projectId, limit, cancellationToken);

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var existing = await _inner.GetAsync(id, cancellationToken).ConfigureAwait(false);

        await _inner.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        // Mirror removal happens after the authoritative delete so a crash in
        // between leaves an orphaned file rather than a dangling index row.
        _mirror.Delete(existing?.MarkdownPath);
    }
}
