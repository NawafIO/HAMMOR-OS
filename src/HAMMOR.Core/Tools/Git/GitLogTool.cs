namespace HAMMOR.Core.Tools.Git;

public sealed class GitLogTool : ITool
{
    private readonly SafeGitRunner _runner;

    public GitLogTool(SafeGitRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public string Name => "git.log";
    public string Description => "Show recent commit log for an allowed repository path. Read-only, bounded to 50 entries.";
    public ToolPermission Permission => ToolPermission.Read;

    public ToolInputSchema InputSchema { get; } = new("""
        {
          "type": "object",
          "properties": {
            "repositoryPath": { "type": "string", "description": "Path to the repository root (must be inside allowed roots)." },
            "maxCount": { "type": "integer", "description": "Maximum commits to return (1-50). Defaults to 50.", "minimum": 1, "maximum": 50 }
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
        if (invocation.Arguments.TryGetValue("maxCount", out var rawMax) && !string.IsNullOrWhiteSpace(rawMax))
        {
            if (!int.TryParse(rawMax, out var parsed) || parsed is < 1 or > 50)
            {
                return ToolValidationResult.Invalid("'maxCount' must be between 1 and 50.");
            }
        }
        return ToolValidationResult.Valid;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var v = Validate(invocation);
        if (!v.IsValid) return ToolResult.Failure(v.Error!);
        var maxCount = invocation.GetInt32("maxCount", 50);
        maxCount = Math.Clamp(maxCount, 1, 50);
        var raw = invocation.GetString("repositoryPath")!;
        var result = await _runner.GetLogAsync(raw, maxCount, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            var msg = result.Error ?? result.StdErr;
            if (!string.IsNullOrWhiteSpace(msg)) return ToolResult.Failure(msg);
            return ToolResult.Failure($"git log failed (exit {result.ExitCode}).");
        }
        var output = result.StdOut;
        if (string.IsNullOrWhiteSpace(output)) return ToolResult.Success("No commits.");
        if (result.Truncated) output += $"{Environment.NewLine}[truncated]";
        return ToolResult.Success(output);
    }
}
