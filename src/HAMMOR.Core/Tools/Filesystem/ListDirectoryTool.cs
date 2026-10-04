using System.IO;
using System.Text;

namespace HAMMOR.Core.Tools.Filesystem;

/// <summary>
/// Lists the contents of a directory. Read-only, bounded, and never throws
/// for expected filesystem conditions.
/// </summary>
public sealed class ListDirectoryTool : ITool
{
    private const int MaxEntries = 5_000;
    private const int MaxDepth = 16;

    public string Name => "filesystem.list_directory";

    public string Description =>
        "List files and folders in a directory. Use when you need to see what exists at a path before reading or writing. "
        + "Set recursive=true to enumerate subfolders up to a bounded depth.";

    public ToolPermission Permission => ToolPermission.Read;

    public ToolInputSchema InputSchema { get; } = new("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Directory path to list." },
            "recursive": { "type": "boolean", "description": "Recurse into subdirectories. Defaults to false." }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """);

    public ToolValidationResult Validate(ToolInvocation invocation)
    {
        var path = invocation.GetString("path");
        if (path is null)
        {
            return ToolValidationResult.Invalid("'path' is required and must not be blank.");
        }

        if (path.Length > 32767)
        {
            return ToolValidationResult.Invalid("'path' is too long.");
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return ToolValidationResult.Invalid("'path' contains invalid characters.");
        }

        if (invocation.Arguments.TryGetValue("recursive", out var raw)
            && !string.IsNullOrWhiteSpace(raw)
            && !bool.TryParse(raw, out _))
        {
            return ToolValidationResult.Invalid("'recursive' must be true or false when provided.");
        }

        return ToolValidationResult.Valid;
    }

    public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var validation = Validate(invocation);
        if (!validation.IsValid)
        {
            return Task.FromResult(ToolResult.Failure(validation.Error!));
        }

        var directoryPath = invocation.GetString("path")!;
        var recursive = false;
        if (invocation.Arguments.TryGetValue("recursive", out var recRaw)
            && !string.IsNullOrWhiteSpace(recRaw))
        {
            bool.TryParse(recRaw, out recursive);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(directoryPath);
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Failure($"Invalid path '{directoryPath}': {ex.Message}"));
        }
        catch (PathTooLongException ex)
        {
            return Task.FromResult(ToolResult.Failure($"Path too long '{directoryPath}': {ex.Message}"));
        }
        catch (NotSupportedException ex)
        {
            return Task.FromResult(ToolResult.Failure($"Path not supported '{directoryPath}': {ex.Message}"));
        }

        try
        {
            if (File.Exists(fullPath))
            {
                return Task.FromResult(ToolResult.Failure($"Path is a file, not a directory: '{fullPath}'."));
            }

            if (!Directory.Exists(fullPath))
            {
                return Task.FromResult(ToolResult.Failure($"Directory does not exist: '{fullPath}'."));
            }

            var entries = recursive
                ? EnumerateRecursive(fullPath, cancellationToken)
                : EnumerateShallow(fullPath);

            if (entries.Count == 0)
            {
                return Task.FromResult(ToolResult.Success($"Directory '{fullPath}' is empty."));
            }

            var truncated = entries.Count >= MaxEntries;
            var output = Format(entries, fullPath, recursive, truncated);
            return Task.FromResult(ToolResult.Success(output));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(ToolResult.Failure($"Access denied for '{fullPath}': {ex.Message}"));
        }
        catch (PathTooLongException ex)
        {
            return Task.FromResult(ToolResult.Failure($"Path too long '{fullPath}': {ex.Message}"));
        }
        catch (IOException ex)
        {
            return Task.FromResult(ToolResult.Failure($"I/O error for '{fullPath}': {ex.Message}"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Failure($"Unexpected error listing '{fullPath}': {ex.Message}"));
        }
    }

    private static List<FileSystemInfo> EnumerateShallow(string root)
    {
        var directory = new DirectoryInfo(root);
        var infos = directory.EnumerateFileSystemInfos();
        var result = new List<FileSystemInfo>();
        foreach (var info in infos)
        {
            cancellationCheck();
            result.Add(info);
            if (result.Count >= MaxEntries)
            {
                break;
            }
        }

        return result;

        static void cancellationCheck() { }
    }

    private static List<FileSystemInfo> EnumerateRecursive(string root, CancellationToken cancellationToken)
    {
        var result = new List<FileSystemInfo>();
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (current, depth) = stack.Pop();
            if (depth > MaxDepth)
            {
                continue;
            }

            IEnumerable<FileSystemInfo> children;
            try
            {
                children = new DirectoryInfo(current).EnumerateFileSystemInfos();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (result.Count >= MaxEntries)
                {
                    return result;
                }

                result.Add(child);

                var isDirectory = (child.Attributes & FileAttributes.Directory) != 0;
                var isReparse = (child.Attributes & FileAttributes.ReparsePoint) != 0;

                // Never follow reparse points / symlinks recursively.
                if (isDirectory && !isReparse && depth + 1 <= MaxDepth)
                {
                    stack.Push((child.FullName, depth + 1));
                }
            }
        }

        return result;
    }

    private static string Format(List<FileSystemInfo> entries, string root, bool recursive, bool truncated)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{entries.Count} entr{(entries.Count == 1 ? "y" : "ies")} in '{root}'{(recursive ? " (recursive" : " (non-recursive")}{(truncated ? ", truncated" : "")}):");
        builder.AppendLine();

        foreach (var entry in entries.OrderBy(e => e.Attributes.HasFlag(FileAttributes.Directory) ? 0 : 1).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            var isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
            if (isDirectory)
            {
                builder.AppendLine($"- {entry.Name}/ [Directory] {entry.Attributes} Modified: {entry.LastWriteTimeUtc:yyyy-MM-ddTHH:mm:ssZ}");
            }
            else if (entry is FileInfo file)
            {
                // FileInfo may throw for size on some virtual files; guard.
                string sizeText;
                try
                {
                    sizeText = $"{file.Length} bytes";
                }
                catch
                {
                    sizeText = "size unavailable";
                }

                builder.AppendLine($"- {entry.Name} [File] {sizeText}, {entry.Attributes} Modified: {entry.LastWriteTimeUtc:yyyy-MM-ddTHH:mm:ssZ}");
            }
            else
            {
                builder.AppendLine($"- {entry.Name} [{entry.Attributes}]");
            }
        }

        if (truncated)
        {
            builder.AppendLine();
            builder.AppendLine($"Output truncated to {MaxEntries} entries and depth {MaxDepth}. Narrow the path or list without recursion.");
        }

        return builder.ToString().TrimEnd();
    }
}
