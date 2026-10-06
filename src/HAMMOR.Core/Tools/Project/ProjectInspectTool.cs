using HAMMOR.Core.Tools.Filesystem;

namespace HAMMOR.Core.Tools.Project;

/// <summary>
/// METADATA-ONLY inspection. Detects existence/names of top-level entries and
/// a bounded set of well-known markers. Never reads file contents and never
/// recursively traverses the workspace. Every inspected path is independently
/// validated through <see cref="IFilesystemPolicy"/>.
/// </summary>
public sealed class ProjectInspectTool : ITool, IPathScopedTool
{
    private readonly IFilesystemPolicy _policy;

    /// <summary>
    /// Explicitly known workspace markers/entry-points inspected besides the
    /// top-level listing. Bounded and fixed — callers cannot supply arbitrary
    /// paths to scan.
    /// </summary>
    private static readonly IReadOnlyList<string> KnownMarkers = new[]
    {
        "README.md",
        "HAMMOR.sln",
        "global.json",
        "Directory.Build.props",
        "Directory.Packages.props",
        ".gitignore",
        "docs",
        "docs/adr",
        "src",
        "src/HAMMOR.Core",
        "src/HAMMOR.App",
        "src/HAMMOR.Infrastructure",
        "src/HAMMOR.Platform.Windows",
        "tests",
    };

    public ProjectInspectTool(IFilesystemPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
    }

    public string Name => "project.inspect";

    public IReadOnlyList<string> PathArguments { get; } = ["projectPath"];
    public string Description => "Inspect project root metadata: top-level entries and known markers. Metadata-only, bounded, no content reads.";
    public ToolPermission Permission => ToolPermission.Read;

    public ToolInputSchema InputSchema { get; } = new("""
        {
          "type": "object",
          "properties": {
            "projectPath": { "type": "string", "description": "Path to the project root (must be inside allowed roots)." }
          },
          "required": ["projectPath"],
          "additionalProperties": false
        }
        """);

    public ToolValidationResult Validate(ToolInvocation invocation)
    {
        var path = invocation.GetString("projectPath");
        if (path is null) return ToolValidationResult.Invalid("'projectPath' is required and must not be blank.");
        if (path.Length > 32767) return ToolValidationResult.Invalid("'projectPath' is too long.");
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return ToolValidationResult.Invalid("'projectPath' contains invalid characters.");
        return ToolValidationResult.Valid;
    }

    public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var v = Validate(invocation);
        if (!v.IsValid) return Task.FromResult(ToolResult.Failure(v.Error!));

        var raw = invocation.GetString("projectPath")!;
        var policyResult = _policy.Validate(raw);
        if (!policyResult.IsAllowed)
        {
            return Task.FromResult(ToolResult.Failure(policyResult.Error!));
        }

        var canonical = policyResult.NormalizedPath;

        try
        {
            if (!Directory.Exists(canonical))
            {
                return Task.FromResult(ToolResult.Failure($"Directory does not exist: '{canonical}'."));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Failure($"Invalid project path '{canonical}': {ex.Message}"));
        }

        try
        {
            var lines = new List<string>();

            // Top-level only — never recurse.
            IReadOnlyList<FileSystemInfo> topLevel;
            try
            {
                topLevel = new DirectoryInfo(canonical).EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Failure($"Failed to list '{canonical}': {ex.Message}"));
            }

            lines.Add($"Project root: '{canonical}'");
            lines.Add($"Top-level entries ({topLevel.Count}):");
            foreach (var entry in topLevel)
            {
                var kind = entry.Attributes.HasFlag(FileAttributes.Directory) ? "dir" : "file";
                lines.Add($"- {entry.Name} [{kind}]");
            }

            lines.Add(string.Empty);
            lines.Add("Known markers:");

            foreach (var marker in KnownMarkers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var candidate = Path.Combine(canonical, marker);

                // Every discovered file must independently pass policy validation.
                var markerValidation = _policy.Validate(candidate);
                if (!markerValidation.IsAllowed)
                {
                    lines.Add($"- {marker}: (outside allowed roots — not inspected)");
                    continue;
                }

                var markerCanonical = markerValidation.NormalizedPath;

                bool isFile = false;
                bool isDir = false;
                try { isFile = File.Exists(markerCanonical); } catch { }
                try { isDir = Directory.Exists(markerCanonical); } catch { }

                if (isFile) lines.Add($"- {marker}: file present");
                else if (isDir) lines.Add($"- {marker}: directory present");
                else lines.Add($"- {marker}: not present");
            }

            // No file contents are ever read — existence/names only.

            return Task.FromResult(ToolResult.Success(string.Join(Environment.NewLine, lines)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Failure($"Unexpected error inspecting '{canonical}': {ex.Message}"));
        }
    }
}
