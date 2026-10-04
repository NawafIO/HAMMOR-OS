using System.IO;

namespace HAMMOR.Core.Tools.Filesystem;

/// <summary>
/// Deletes a single file. Declared Destructive so the permission engine always
/// requires explicit confirmation before it runs.
/// </summary>
public sealed class DeleteFileTool : ITool
{
    public string Name => "filesystem.delete_file";

    public string Description =>
        "Delete a single file. Requires explicit confirmation and cannot delete directories or recurse.";

    public ToolPermission Permission => ToolPermission.Destructive;

    public ToolInputSchema InputSchema { get; } = new("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "File path to delete." }
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

        return ToolValidationResult.Valid;
    }

    public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var validation = Validate(invocation);
        if (!validation.IsValid)
        {
            return Task.FromResult(ToolResult.Failure(validation.Error!));
        }

        var rawPath = invocation.GetString("path")!;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath);
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Failure($"Invalid path '{rawPath}': {ex.Message}"));
        }
        catch (PathTooLongException ex)
        {
            return Task.FromResult(ToolResult.Failure($"Path too long '{rawPath}': {ex.Message}"));
        }
        catch (NotSupportedException ex)
        {
            return Task.FromResult(ToolResult.Failure($"Path not supported '{rawPath}': {ex.Message}"));
        }

        try
        {
            if (Directory.Exists(fullPath))
            {
                return Task.FromResult(ToolResult.Failure($"Path is a directory; this tool cannot delete directories: '{fullPath}'."));
            }

            if (!File.Exists(fullPath))
            {
                return Task.FromResult(ToolResult.Failure($"File does not exist: '{fullPath}'."));
            }

            File.Delete(fullPath);

            return Task.FromResult(ToolResult.Success($"Deleted '{fullPath}'."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(ToolResult.Failure($"Access denied for '{fullPath}': {ex.Message}"));
        }
        catch (PathTooLongException ex)
        {
            return Task.FromResult(ToolResult.Failure($"Path too long '{fullPath}': {ex.Message}"));
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(ToolResult.Failure($"Invalid path '{fullPath}': {ex.Message}"));
        }
        catch (NotSupportedException ex)
        {
            return Task.FromResult(ToolResult.Failure($"Path not supported '{fullPath}': {ex.Message}"));
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
            return Task.FromResult(ToolResult.Failure($"Unexpected error deleting '{fullPath}': {ex.Message}"));
        }
    }
}
