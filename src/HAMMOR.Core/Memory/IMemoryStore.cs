namespace HAMMOR.Core.Memory;

/// <summary>
/// Persistent memory. SQLite holds the structured index; the markdown mirror
/// keeps the same content human-readable and portable.
/// </summary>
public interface IMemoryStore
{
    Task<MemoryEntry> SaveAsync(MemoryEntry entry, CancellationToken cancellationToken = default);

    Task<MemoryEntry?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Full-text search over title and content, newest first.</summary>
    Task<IReadOnlyList<MemoryEntry>> SearchAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recent entries, optionally narrowed to one project.
    /// </summary>
    Task<IReadOnlyList<MemoryEntry>> GetRecentAsync(
        string? projectId = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>One remembered item.</summary>
public sealed record MemoryEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");

    public required string Title { get; init; }

    public required string Content { get; init; }

    /// <summary>Project association, or null for global memory.</summary>
    public string? ProjectId { get; init; }

    /// <summary>Task association, when the memory came out of a task run.</summary>
    public string? TaskId { get; init; }

    public MemoryKind Kind { get; init; } = MemoryKind.Note;

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ModifiedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Relative path of the markdown mirror, when one exists. Null for
    /// entries that live only in SQLite.
    /// </summary>
    public string? MarkdownPath { get; init; }

    /// <summary>
    /// SHA-256 of <see cref="Content"/>, used by the incremental indexer to
    /// skip unchanged files instead of rebuilding the whole index.
    /// </summary>
    public string? ContentHash { get; init; }
}

public enum MemoryKind
{
    Note = 0,
    Fact = 1,
    Preference = 2,
    ProjectContext = 3,
    Conversation = 4,
}
