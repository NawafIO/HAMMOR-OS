using HAMMOR.Core.Memory;
using Microsoft.Data.Sqlite;

namespace HAMMOR.Infrastructure.Persistence;

/// <summary>
/// Structured memory index in SQLite. Pure persistence — the markdown mirror
/// is layered on top by <see cref="Memory.MarkdownMirroringMemoryStore"/>.
/// </summary>
public sealed class SqliteMemoryStore(SqliteDatabase database) : IMemoryStore
{
    private const string Columns =
        "id, title, content, project_id, task_id, kind, created_utc, modified_utc, "
        + "markdown_path, content_hash";

    private readonly SqliteDatabase _database =
        database ?? throw new ArgumentNullException(nameof(database));

    public async Task<MemoryEntry> SaveAsync(
        MemoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        // Upsert so callers can save the same id repeatedly without first
        // checking whether it exists.
        command.CommandText = """
            INSERT INTO memory
                (id, title, content, project_id, task_id, kind, created_utc,
                 modified_utc, markdown_path, content_hash)
            VALUES
                ($id, $title, $content, $project, $task, $kind, $created,
                 $modified, $markdownPath, $contentHash)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                content = excluded.content,
                project_id = excluded.project_id,
                task_id = excluded.task_id,
                kind = excluded.kind,
                modified_utc = excluded.modified_utc,
                markdown_path = excluded.markdown_path,
                content_hash = excluded.content_hash;
            """;

        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$title", entry.Title);
        command.Parameters.AddWithValue("$content", entry.Content);
        command.Parameters.AddWithValue("$project", entry.ProjectId.OrDbNull());
        command.Parameters.AddWithValue("$task", entry.TaskId.OrDbNull());
        command.Parameters.AddWithValue("$kind", (int)entry.Kind);
        command.Parameters.AddWithValue("$created", entry.CreatedUtc.ToStorage());
        command.Parameters.AddWithValue("$modified", entry.ModifiedUtc.ToStorage());
        command.Parameters.AddWithValue("$markdownPath", entry.MarkdownPath.OrDbNull());
        command.Parameters.AddWithValue("$contentHash", entry.ContentHash.OrDbNull());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return entry;
    }

    public async Task<MemoryEntry?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        command.CommandText = $"SELECT {Columns} FROM memory WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? Map(reader)
            : null;
    }

    public async Task<IReadOnlyList<MemoryEntry>> SearchAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT {Columns} FROM memory
            WHERE title LIKE $pattern ESCAPE '\' OR content LIKE $pattern ESCAPE '\'
            ORDER BY modified_utc DESC
            LIMIT $limit;
            """;

        // Escape LIKE wildcards so a query containing % or _ is matched
        // literally instead of turning into a table scan pattern.
        var escaped = query
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        command.Parameters.AddWithValue("$pattern", $"%{escaped}%");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));

        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MemoryEntry>> GetRecentAsync(
        string? projectId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        var where = string.IsNullOrWhiteSpace(projectId) ? string.Empty : "WHERE project_id = $project";

        command.CommandText =
            $"SELECT {Columns} FROM memory {where} ORDER BY created_utc DESC LIMIT $limit;";

        if (!string.IsNullOrWhiteSpace(projectId))
        {
            command.Parameters.AddWithValue("$project", projectId);
        }

        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));

        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        command.CommandText = "DELETE FROM memory WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<MemoryEntry>> ReadAllAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var entries = new List<MemoryEntry>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(Map(reader));
        }

        return entries;
    }

    private static MemoryEntry Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetStringValue("id"),
        Title = reader.GetStringValue("title"),
        Content = reader.GetStringValue("content"),
        ProjectId = reader.GetNullableString("project_id"),
        TaskId = reader.GetNullableString("task_id"),
        Kind = (MemoryKind)reader.GetInt("kind"),
        CreatedUtc = reader.GetTimestamp("created_utc"),
        ModifiedUtc = reader.GetTimestamp("modified_utc"),
        MarkdownPath = reader.GetNullableString("markdown_path"),
        ContentHash = reader.GetNullableString("content_hash"),
    };
}
