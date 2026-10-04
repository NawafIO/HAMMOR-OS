using System.IO;
using System.Text;

namespace HAMMOR.Core.Tools.Filesystem;

/// <summary>
/// Creates or replaces a text file with bounded content. Uses an atomic
/// write (temp file + move) so a failure does not leave a half-written file.
/// </summary>
public sealed class WriteFileTool : ITool
{
    private const int MaxContentChars = 2_000_000; // ~2 MB UTF-8
    private const long MaxContentBytes = 2 * 1024 * 1024;

    public string Name => "filesystem.write_file";

    public string Description =>
        "Write text to a file, creating parent directories as needed. Fails if the file exists and overwrite is false.";

    public ToolPermission Permission => ToolPermission.Write;

    public ToolInputSchema InputSchema { get; } = new("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "File path to write." },
            "content": { "type": "string", "description": "Text content to write (UTF-8)." },
            "overwrite": { "type": "boolean", "description": "Allow overwriting an existing file. Defaults to false." }
          },
          "required": ["path", "content"],
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

        // Content is required. Distinguish missing (null) from empty — empty is
        // allowed so the tool can create an empty file.
        if (!invocation.Arguments.ContainsKey("content") || invocation.Arguments["content"] is null)
        {
            return ToolValidationResult.Invalid("'content' is required.");
        }

        var content = invocation.Arguments["content"]!;

        if (content.Length > MaxContentChars)
        {
            return ToolValidationResult.Invalid($"'content' is too large ({content.Length} characters). Maximum is {MaxContentChars}.");
        }

        if (Encoding.UTF8.GetByteCount(content) > MaxContentBytes)
        {
            return ToolValidationResult.Invalid($"'content' is too large ({Encoding.UTF8.GetByteCount(content)} bytes UTF-8). Maximum is {MaxContentBytes} bytes.");
        }

        if (invocation.Arguments.TryGetValue("overwrite", out var overwriteRaw)
            && !string.IsNullOrWhiteSpace(overwriteRaw)
            && !bool.TryParse(overwriteRaw, out _))
        {
            return ToolValidationResult.Invalid("'overwrite' must be true or false when provided.");
        }

        return ToolValidationResult.Valid;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var validation = Validate(invocation);
        if (!validation.IsValid)
        {
            return ToolResult.Failure(validation.Error!);
        }

        var rawPath = invocation.GetString("path")!;
        var content = invocation.Arguments["content"]!;

        var overwrite = false;
        if (invocation.Arguments.TryGetValue("overwrite", out var overwriteRaw)
            && !string.IsNullOrWhiteSpace(overwriteRaw))
        {
            bool.TryParse(overwriteRaw, out overwrite);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath);
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Failure($"Invalid path '{rawPath}': {ex.Message}");
        }
        catch (PathTooLongException ex)
        {
            return ToolResult.Failure($"Path too long '{rawPath}': {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            return ToolResult.Failure($"Path not supported '{rawPath}': {ex.Message}");
        }

        try
        {
            if (Directory.Exists(fullPath))
            {
                return ToolResult.Failure($"Path is a directory, not a file: '{fullPath}'.");
            }

            if (!overwrite && File.Exists(fullPath))
            {
                return ToolResult.Failure($"File already exists and overwrite is false: '{fullPath}'.");
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                // Created only after validation, so an invalid invocation does
                // not create directories as a side effect.
                Directory.CreateDirectory(directory);
            }

            // Atomic write: temp file in the same directory, then move.
            var tempPath = Path.Combine(
                directory ?? Path.GetTempPath(),
                $".hammor-write-{Guid.NewGuid():N}.tmp");

            try
            {
                await File.WriteAllTextAsync(tempPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
                    .ConfigureAwait(false);

                // File.Move with overwrite parameter (.NET 8) is atomic on same volume.
                File.Move(tempPath, fullPath, overwrite);
            }
            finally
            {
                // Best-effort cleanup of the temp file if the move failed.
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Suppress cleanup failures; the main error (if any) is
                    // already being returned.
                }
            }

            var writtenBytes = Encoding.UTF8.GetByteCount(content);
            return ToolResult.Success(
                overwrite
                    ? $"Wrote {writtenBytes} bytes to '{fullPath}' (overwritten)."
                    : $"Created '{fullPath}' ({writtenBytes} bytes).");
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolResult.Failure($"Access denied for '{fullPath}': {ex.Message}");
        }
        catch (PathTooLongException ex)
        {
            return ToolResult.Failure($"Path too long '{fullPath}': {ex.Message}");
        }
        catch (DirectoryNotFoundException ex)
        {
            return ToolResult.Failure($"Directory not found for '{fullPath}': {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Failure($"Invalid path '{fullPath}': {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            return ToolResult.Failure($"Path not supported '{fullPath}': {ex.Message}");
        }
        catch (IOException ex)
        {
            return ToolResult.Failure($"I/O error for '{fullPath}': {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"Unexpected error writing '{fullPath}': {ex.Message}");
        }
    }
}
