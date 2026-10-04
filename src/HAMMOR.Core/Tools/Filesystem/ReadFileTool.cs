using System.IO;
using System.Text;

namespace HAMMOR.Core.Tools.Filesystem;

/// <summary>
/// Reads a bounded window of a text file. Gated by <see cref="IFilesystemPolicy"/> when supplied.
/// Never loads an unbounded file into memory and never treats opaque binary as text.
/// </summary>
public sealed class ReadFileTool : ITool
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private const int DefaultMaxLines = 5000;
    private const int HardMaxLines = 10000;
    private const int BinaryScanBytes = 8192;

    private readonly IFilesystemPolicy? _policy;

    public ReadFileTool(IFilesystemPolicy? policy = null)
    {
        _policy = policy;
    }

    public string Name => "filesystem.read_file";

    public string Description =>
        "Read a text file with bounded size and optional line windowing. Use offset and limit to page through large files.";

    public ToolPermission Permission => ToolPermission.Read;

    public ToolInputSchema InputSchema { get; } = new("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "File path to read." },
            "encoding": { "type": "string", "description": "Text encoding name. Defaults to utf-8. One of: utf-8, utf8, ascii, utf-16, utf-16le, utf-16be." },
            "offset": { "type": "integer", "description": "Zero-based line offset. Defaults to 0.", "minimum": 0 },
            "limit": { "type": "integer", "description": "Maximum lines to return (1-10000). Defaults to 5000.", "minimum": 1, "maximum": 10000 }
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

        var encodingName = invocation.GetString("encoding");
        if (encodingName is not null && ResolveEncoding(encodingName) is null)
        {
            return ToolValidationResult.Invalid(
                $"'encoding' '{encodingName}' is not supported. Use one of: utf-8, utf8, ascii, utf-16, utf-16le, utf-16be.");
        }

        if (invocation.Arguments.TryGetValue("offset", out var offsetRaw) && !string.IsNullOrWhiteSpace(offsetRaw))
        {
            if (!int.TryParse(offsetRaw, out var offset) || offset < 0)
            {
                return ToolValidationResult.Invalid("'offset' must be an integer >= 0.");
            }
        }

        if (invocation.Arguments.TryGetValue("limit", out var limitRaw) && !string.IsNullOrWhiteSpace(limitRaw))
        {
            if (!int.TryParse(limitRaw, out var limit) || limit is < 1 or > HardMaxLines)
            {
                return ToolValidationResult.Invalid($"'limit' must be between 1 and {HardMaxLines}.");
            }
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
        var encodingName = invocation.GetString("encoding");
        var encoding = ResolveEncoding(encodingName ?? "utf-8")!;

        var offset = 0;
        if (invocation.Arguments.TryGetValue("offset", out var offsetRaw) && !string.IsNullOrWhiteSpace(offsetRaw))
        {
            int.TryParse(offsetRaw, out offset);
        }

        var limit = DefaultMaxLines;
        if (invocation.Arguments.TryGetValue("limit", out var limitRaw) && !string.IsNullOrWhiteSpace(limitRaw))
        {
            int.TryParse(limitRaw, out limit);
            limit = Math.Clamp(limit, 1, HardMaxLines);
        }

        string fullPath;
        if (_policy is not null)
        {
            var pr = _policy.Validate(rawPath);
            if (!pr.IsAllowed)
            {
                return ToolResult.Failure(pr.Error!);
            }

            fullPath = pr.NormalizedPath;
        }
        else
        {
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
        }

        try
        {
            if (Directory.Exists(fullPath))
            {
                return ToolResult.Failure($"Path is a directory, not a file: '{fullPath}'.");
            }

            var fileInfo = new FileInfo(fullPath);
            if (!fileInfo.Exists)
            {
                return ToolResult.Failure($"File does not exist: '{fullPath}'.");
            }

            if (fileInfo.Length > MaxBytes)
            {
                return ToolResult.Failure(
                    $"File too large ({fileInfo.Length} bytes). Maximum readable size is {MaxBytes} bytes. "
                    + "Use offset/limit to page if the file grows, or choose a smaller file.");
            }

            var looksBinary = await LooksBinaryAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (looksBinary)
            {
                return ToolResult.Failure($"File appears to be binary and cannot be read as text: '{fullPath}'.");
            }

            var lines = new List<string>(Math.Min(limit, 1024));
            var totalLines = 0;
            var truncated = false;

            using (var reader = new StreamReader(fullPath, encoding, detectEncodingFromByteOrderMarks: true))
            {
                string? line;
                while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
                {
                    if (totalLines < offset)
                    {
                        totalLines++;
                        continue;
                    }

                    if (lines.Count >= limit)
                    {
                        truncated = true;
                        totalLines++;
                        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
                        {
                            totalLines++;
                        }

                        break;
                    }

                    lines.Add(line);
                    totalLines++;
                }
            }

            if (offset > 0 && lines.Count == 0 && totalLines <= offset)
            {
                return ToolResult.Failure($"Offset {offset} is beyond end of file ({totalLines} lines): '{fullPath}'.");
            }

            var header =
                $"{fullPath} — {lines.Count} line(s) shown"
                + (offset > 0 ? $" from offset {offset}" : string.Empty)
                + (truncated ? $" (truncated, {totalLines} total lines, limit {limit})" : string.Empty)
                + $"{Environment.NewLine}{new string('-', 60)}{Environment.NewLine}";

            var body = lines.Count == 0 ? "(empty or no lines in requested window)" : string.Join(Environment.NewLine, lines);

            return ToolResult.Success(header + body);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolResult.Failure($"Access denied for '{fullPath}': {ex.Message}");
        }
        catch (PathTooLongException ex)
        {
            return ToolResult.Failure($"Path too long '{fullPath}': {ex.Message}");
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
            return ToolResult.Failure($"Unexpected error reading '{fullPath}': {ex.Message}");
        }
    }

    private static Encoding? ResolveEncoding(string name)
    {
        var key = name.Trim().ToLowerInvariant();
        return key switch
        {
            "utf-8" or "utf8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            "ascii" => Encoding.ASCII,
            "utf-16" or "utf16" or "utf-16le" or "utf16le" => Encoding.Unicode,
            "utf-16be" or "utf16be" => Encoding.BigEndianUnicode,
            _ => null,
        };
    }

    private static async Task<bool> LooksBinaryAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var toRead = (int)Math.Min(BinaryScanBytes, stream.Length);
            if (toRead == 0)
            {
                return false;
            }

            var buffer = new byte[toRead];
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
