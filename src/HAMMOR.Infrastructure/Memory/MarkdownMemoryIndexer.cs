using HAMMOR.Infrastructure.Persistence;
using HAMMOR.Core.Storage;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Infrastructure.Memory;

/// <summary>
/// Scans the markdown memory folder and records which files have been seen.
/// </summary>
/// <remarks>
/// Deliberately incremental. A file is only re-read when its size or
/// modification time differs from the recorded values, and only re-indexed
/// when the content hash actually changed. Rebuilding the whole index on every
/// launch would make startup cost grow with memory size for no benefit.
/// </remarks>
public sealed class MarkdownMemoryIndexer(
    SqliteDatabase database,
    HammorPaths paths,
    ILogger<MarkdownMemoryIndexer> logger)
{
    private readonly SqliteDatabase _database =
        database ?? throw new ArgumentNullException(nameof(database));

    private readonly HammorPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    private readonly ILogger<MarkdownMemoryIndexer> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Reconciles the index with the folder and returns what changed.
    /// </summary>
    public async Task<IndexingReport> ReindexAsync(CancellationToken cancellationToken = default)
    {
        var root = _paths.MemoryDirectory;

        if (!Directory.Exists(root))
        {
            return new IndexingReport(0, 0, 0, 0);
        }

        var known = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var scanned = 0;
        var added = 0;
        var updated = 0;

        foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanned++;

            var relativePath = Path.GetRelativePath(root, file);
            seen.Add(relativePath);

            var info = new FileInfo(file);
            var modifiedUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);

            // Cheap gate first: unchanged size and mtime means skip the read
            // entirely. Hashing is only paid when something plausibly changed.
            if (known.TryGetValue(relativePath, out var entry)
                && entry.SizeBytes == info.Length
                && entry.ModifiedUtc == modifiedUtc)
            {
                continue;
            }

            string content;
            try
            {
                content = await File.ReadAllTextAsync(file, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Skipping unreadable memory file {Path}.", relativePath);
                continue;
            }

            var hash = MarkdownMemoryMirror.ComputeHash(content);

            // Second gate: mtime moved but content is identical (a touch, or a
            // rewrite with the same bytes). Record the new stat, do no work.
            if (entry is not null && entry.ContentHash == hash)
            {
                await UpsertAsync(relativePath, info.Length, modifiedUtc, hash, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            await UpsertAsync(relativePath, info.Length, modifiedUtc, hash, cancellationToken)
                .ConfigureAwait(false);

            if (entry is null)
            {
                added++;
            }
            else
            {
                updated++;
            }
        }

        var removed = await RemoveMissingAsync(known.Keys, seen, cancellationToken)
            .ConfigureAwait(false);

        var report = new IndexingReport(scanned, added, updated, removed);

        _logger.LogInformation(
            "Markdown index: scanned {Scanned}, added {Added}, updated {Updated}, removed {Removed}.",
            report.Scanned, report.Added, report.Updated, report.Removed);

        return report;
    }

    private async Task<Dictionary<string, IndexedFile>> LoadIndexAsync(
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IndexedFile>(StringComparer.OrdinalIgnoreCase);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT relative_path, size_bytes, modified_utc, content_hash FROM markdown_index;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var path = reader.GetStringValue("relative_path");

            result[path] = new IndexedFile(
                path,
                reader.GetLong("size_bytes"),
                reader.GetTimestamp("modified_utc"),
                reader.GetStringValue("content_hash"));
        }

        return result;
    }

    private async Task UpsertAsync(
        string relativePath,
        long sizeBytes,
        DateTimeOffset modifiedUtc,
        string contentHash,
        CancellationToken cancellationToken)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO markdown_index
                (relative_path, size_bytes, modified_utc, content_hash, indexed_utc)
            VALUES
                ($path, $size, $modified, $hash, $indexed)
            ON CONFLICT(relative_path) DO UPDATE SET
                size_bytes = excluded.size_bytes,
                modified_utc = excluded.modified_utc,
                content_hash = excluded.content_hash,
                indexed_utc = excluded.indexed_utc;
            """;

        command.Parameters.AddWithValue("$path", relativePath);
        command.Parameters.AddWithValue("$size", sizeBytes);
        command.Parameters.AddWithValue("$modified", modifiedUtc.ToStorage());
        command.Parameters.AddWithValue("$hash", contentHash);
        command.Parameters.AddWithValue("$indexed", DateTimeOffset.UtcNow.ToStorage());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> RemoveMissingAsync(
        IEnumerable<string> knownPaths,
        HashSet<string> seenPaths,
        CancellationToken cancellationToken)
    {
        var missing = knownPaths.Where(p => !seenPaths.Contains(p)).ToList();
        if (missing.Count == 0)
        {
            return 0;
        }

        await using var connection = _database.OpenConnection();

        foreach (var path in missing)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM markdown_index WHERE relative_path = $path;";
            command.Parameters.AddWithValue("$path", path);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return missing.Count;
    }

    private sealed record IndexedFile(
        string RelativePath,
        long SizeBytes,
        DateTimeOffset ModifiedUtc,
        string ContentHash);
}

/// <param name="Scanned">Markdown files found on disk.</param>
/// <param name="Added">Files indexed for the first time.</param>
/// <param name="Updated">Files whose content hash changed.</param>
/// <param name="Removed">Index rows dropped because the file is gone.</param>
public sealed record IndexingReport(int Scanned, int Added, int Updated, int Removed)
{
    public bool AnyChanges => Added > 0 || Updated > 0 || Removed > 0;
}
