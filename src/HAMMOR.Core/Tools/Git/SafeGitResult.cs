namespace HAMMOR.Core.Tools.Git;

/// <summary>
/// Bounded result of a git invocation. StdOut/StdErr are truncated when the
/// process exceeds the internal byte limit.
/// </summary>
public sealed record SafeGitResult(
    bool Succeeded,
    int ExitCode,
    string StdOut,
    string StdErr,
    bool Truncated,
    string? Error)
{
    public static SafeGitResult Failure(string error) =>
        new(false, -1, string.Empty, string.Empty, false, error);

    public static SafeGitResult FromProcess(int exitCode, string stdOut, string stdErr, bool truncated) =>
        new(exitCode == 0, exitCode, stdOut, stdErr, truncated, exitCode == 0 ? null : stdErr);
}
