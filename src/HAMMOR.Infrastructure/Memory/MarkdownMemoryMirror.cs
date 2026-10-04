using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HAMMOR.Core.Memory;
using HAMMOR.Core.Storage;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Infrastructure.Memory;

/// <summary>
/// Writes each memory entry to a plain markdown file with a small YAML
/// front-matter header, so memory stays human-readable and portable outside
/// HAMMOR.
/// </summary>
/// <remarks>
/// The markdown is a mirror, not the source of truth: SQLite remains
/// authoritative and the markdown folder is explicitly not a security
/// boundary. Nothing in HAMMOR executes content read from these files.
/// </remarks>
public sealed class MarkdownMemoryMirror(
    HammorPaths paths,
    ILogger<MarkdownMemoryMirror> logger)
{
    private readonly HammorPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    private readonly ILogger<MarkdownMemoryMirror> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Computes the SHA-256 hex digest used for change detection.</summary>
    public static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Writes <paramref name="entry"/> to disk and returns the path relative
    /// to the memory folder, or null when the write failed.
    /// </summary>
    public async Task<string?> WriteAsync(
        MemoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var relativePath = BuildRelativePath(entry);
        var fullPath = Path.Combine(_paths.MemoryDirectory, relativePath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            var document = new StringBuilder()
                .AppendLine("---")
                .AppendLine($"id: {entry.Id}")
                .AppendLine($"title: {EscapeYaml(entry.Title)}")
                .AppendLine($"kind: {entry.Kind}")
                .AppendLine($"created: {entry.CreatedUtc.ToUniversalTime():O}")
                .AppendLine($"modified: {entry.ModifiedUtc.ToUniversalTime():O}");

            if (!string.IsNullOrWhiteSpace(entry.ProjectId))
            {
                document.AppendLine($"project: {entry.ProjectId}");
            }

            if (!string.IsNullOrWhiteSpace(entry.TaskId))
            {
                document.AppendLine($"task: {entry.TaskId}");
            }

            document
                .AppendLine("---")
                .AppendLine()
                .AppendLine($"# {entry.Title}")
                .AppendLine()
                .AppendLine(entry.Content);

            await File.WriteAllTextAsync(
                fullPath, document.ToString(), Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);

            return relativePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The SQLite row is authoritative, so a mirror failure does not
            // lose the memory — but it is still logged as an error rather than
            // ignored, because the user's portable copy is now out of date.
            _logger.LogError(
                ex, "Could not write markdown mirror for memory {MemoryId} to {Path}.",
                entry.Id, fullPath);

            return null;
        }
    }

    /// <summary>Deletes the mirror file for a relative path, if it exists.</summary>
    public void Delete(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var fullPath = Path.Combine(_paths.MemoryDirectory, relativePath);

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete markdown mirror {Path}.", fullPath);
        }
    }

    /// <summary>
    /// Groups entries into <c>yyyy-MM</c> folders with a slugged file name, so
    /// the folder stays navigable as memory accumulates.
    /// </summary>
    private static string BuildRelativePath(MemoryEntry entry)
    {
        var folder = entry.CreatedUtc.ToUniversalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var slug = Slugify(entry.Title);

        // Id suffix guarantees uniqueness even when two titles slug identically.
        return Path.Combine(folder, $"{slug}-{entry.Id[..8]}.md");
    }

    /// <summary>
    /// Produces a filesystem-safe slug. Non-ASCII scripts (Arabic titles are
    /// expected) have no safe transliteration here, so they are dropped and
    /// the id suffix carries uniqueness.
    /// </summary>
    private static string Slugify(string title)
    {
        var builder = new StringBuilder(title.Length);

        foreach (var ch in title)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is ' ' or '-' or '_' && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');

        if (slug.Length > 50)
        {
            slug = slug[..50].TrimEnd('-');
        }

        return slug.Length == 0 ? "note" : slug;
    }

    private static string EscapeYaml(string value) =>
        value.Contains(':', StringComparison.Ordinal) || value.Contains('#', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
}
