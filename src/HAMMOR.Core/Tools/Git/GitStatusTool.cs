namespace HAMMOR.Core.Tools.Git;

public sealed class GitStatusTool : ITool
{
    private readonly SafeGitRunner _runner;

    public GitStatusTool(SafeGitRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public string Name => "git.status";
    public string Description => "Show working-tree status for an allowed repository path. Read-only.";
    public ToolPermission Permission => ToolPermission.Read;

    public ToolInputSchema InputSchema { get; } = new("""
        {
          "type": "object",
          "properties": {
            "repositoryPath": { "type": "string", "description": "Path to the repository root (must be inside allowed roots)." }
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
        return ToolValidationResult.Valid;
    }

    public async Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        var v = Validate(invocation);
        if (!v.IsValid) return ToolResult.Failure(v.Error!);
        var raw = invocation.GetString("repositoryPath")!;
        var result = await _runner.GetStatusAsync(raw, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            var msg = result.Error ?? result.StdErr;
            if (!string.IsNullOrWhiteSpace(msg)) return ToolResult.Failure(msg);
            return ToolResult.Failure($"git status failed (exit {result.ExitCode}).");
        }
        var output = result.StdOut;
        if (string.IsNullOrWhiteSpace(output)) return ToolResult.Success("Working tree clean.");
        if (result.Truncated) output += $"{Environment.NewLine}[truncated]";
        return ToolResult.Success(output);
    }
}
