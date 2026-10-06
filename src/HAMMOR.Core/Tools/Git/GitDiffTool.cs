namespace HAMMOR.Core.Tools.Git;

public sealed class GitDiffTool : ITool, IGitRepositoryScopedTool
{
    private readonly SafeGitRunner _runner;

    public GitDiffTool(SafeGitRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public string Name => "git.diff";

    public IReadOnlyList<string> PathArguments { get; } = ["repositoryPath"];
    public string Description => "Show unstaged/staged diff for an allowed repository path. Read-only, bounded output.";
    public ToolPermission Permission => ToolPermission.Read;

    public ToolInputSchema InputSchema { get; } = new("""
        {
          "type": "object",
          "properties": {
            "repositoryPath": { "type": "string", "description": "Path to the repository root (must be inside allowed roots)." },
            "staged": { "type": "boolean", "description": "When true, show staged changes (git diff --staged). Defaults to false." }
          },
          "required": ["repositoryPath"],
          "additionalProperties": false
        }
        """);

    public ToolValidationResult Validate(ToolInvocation invocation)
    {
        var path = invocation.GetString("repositoryPath");
        if (path is null) return ToolValidationResult.Invalid("'repositoryPath' is required and must not be blank.");
        if (path.Length > 32767) return ToolValidationResult.Invalid("'repositoryPath' is too long.");
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return ToolValidationResult.Invalid("'repositoryPath' contains invalid characters.");
        if (invocation.Arguments.TryGetValue("staged", out var stagedRaw) && !string.IsNullOrWhiteSpace(stagedRaw))
        {
            if (!bool.TryParse(stagedRaw, out _))
            {
                return ToolValidationResult.Invalid("'staged' must be true or false when provided.");
            }
        }
        return ToolValidationResult.Valid;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var v = Validate(invocation);
        if (!v.IsValid) return ToolResult.Failure(v.Error!);
        var staged = false;
        if (invocation.Arguments.TryGetValue("staged", out var stagedRaw) && !string.IsNullOrWhiteSpace(stagedRaw))
        {
            bool.TryParse(stagedRaw, out staged);
        }
        var raw = invocation.GetString("repositoryPath")!;
        var result = await _runner.GetDiffAsync(raw, staged, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            var msg = result.Error ?? result.StdErr;
            if (!string.IsNullOrWhiteSpace(msg)) return ToolResult.Failure(msg);
            return ToolResult.Failure($"git diff failed (exit {result.ExitCode}).");
        }
        var output = result.StdOut;
        if (string.IsNullOrWhiteSpace(output)) return ToolResult.Success("No changes.");
        if (result.Truncated) output += $"{Environment.NewLine}[truncated — output exceeded 256 KiB]";
        return ToolResult.Success(output);
    }
}
