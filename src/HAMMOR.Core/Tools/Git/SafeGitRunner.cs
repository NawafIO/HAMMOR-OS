using System.Diagnostics;
using System.Text;

namespace HAMMOR.Core.Tools.Git;

/// <summary>
/// Executes only a fixed allow-list of git subcommands. There is intentionally
/// no generic command executor. Each public operation builds its own fixed
/// argument list internally; callers cannot supply flags or subcommands.
/// </summary>
/// <remarks>
/// Repository paths are validated through <see cref="IFilesystemPolicy"/> via
/// <see cref="HAMMOR.Core.Tools.Filesystem.IFilesystemPolicy"/>, must be
/// existing directories, and are canonicalised/rebound before
/// <see cref="Process.Start()"/>. Output is bounded so no unbounded stream
/// is accumulated in memory. Process is launched via structured
/// <see cref="ProcessStartInfo.ArgumentList"/> — no shell command string.
/// Git is confined to the validated directory (ADR-005):
/// <see cref="GitRepositoryGuard"/> refuses layouts and config that redirect
/// it elsewhere, and the child environment pins the work tree, common
/// directory and discovery boundary to that directory.
/// </remarks>
public sealed class SafeGitRunner
{
    public const int MaxOutputChars = 256 * 1024;
    internal const int MaxOutputBytesForTestVisibility = MaxOutputChars;

    private readonly HAMMOR.Core.Tools.Filesystem.IFilesystemPolicy _policy;

    public SafeGitRunner(HAMMOR.Core.Tools.Filesystem.IFilesystemPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
    }

    // --ignore-submodules=all: a submodule checkout's .git file can point at any
    // repository, and git would otherwise inspect it (ADR-005).
    public Task<SafeGitResult> GetStatusAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        RunValidatedAsync(repositoryPath, new[] { "status", "--porcelain=v1", "--ignore-submodules=all" }, cancellationToken);

    public Task<SafeGitResult> GetDiffAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        GetDiffAsync(repositoryPath, staged: false, cancellationToken);

    public Task<SafeGitResult> GetDiffAsync(string repositoryPath, bool staged, CancellationToken cancellationToken = default)
    {
        var args = staged
            ? new[] { "diff", "--no-color", "--ignore-submodules=all", "--staged" }
            : new[] { "diff", "--no-color", "--ignore-submodules=all" };
        return RunValidatedAsync(repositoryPath, args, cancellationToken);
    }

    public Task<SafeGitResult> GetLogAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        GetLogAsync(repositoryPath, 50, cancellationToken);

    public Task<SafeGitResult> GetLogAsync(string repositoryPath, int maxCount, CancellationToken cancellationToken = default)
    {
        var bounded = Math.Clamp(maxCount, 1, 50);
        var countText = bounded.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return RunValidatedAsync(repositoryPath, new[] { "log", "--oneline", "-n", countText, "--no-decorate" }, cancellationToken);
    }

    private async Task<SafeGitResult> RunValidatedAsync(string rawPath, IReadOnlyList<string> fixedArgs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return SafeGitResult.Failure("Path must not be blank.");
        }

        var validation = _policy.Validate(rawPath);
        if (!validation.IsAllowed)
        {
            return SafeGitResult.Failure(validation.Error!);
        }

        var canonical = validation.NormalizedPath;

        try
        {
            if (!Directory.Exists(canonical))
            {
                return SafeGitResult.Failure($"Directory does not exist: '{canonical}'.");
            }
        }
        catch (Exception ex)
        {
            return SafeGitResult.Failure($"Invalid repository path '{canonical}': {ex.Message}");
        }

        var problem = GitRepositoryGuard.FindProblem(canonical);
        if (problem is not null)
        {
            return SafeGitResult.Failure(problem);
        }

        return await RunAsync(canonical, fixedArgs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pins git to <paramref name="repositoryRoot"/>, so a <c>core.worktree</c>,
    /// <c>commondir</c> or broken <c>.git</c> that appears after
    /// <see cref="GitRepositoryGuard"/> ran still cannot redirect git.
    /// Inherited <c>GIT_*</c> variables are dropped first: HAMMOR's own
    /// environment must not redirect objects, index or config.
    /// </summary>
    /// <remarks>
    /// <c>GIT_DIR</c> is deliberately not set: an explicit git directory makes
    /// git skip its <c>safe.directory</c> ownership check. The ceiling and
    /// <c>safe.bareRepository=explicit</c> bound discovery to
    /// <c>&lt;root&gt;\.git</c> instead.
    /// </remarks>
    private static void ConfineToRepository(ProcessStartInfo psi, string repositoryRoot)
    {
        var environment = psi.Environment;
        foreach (var key in environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            environment.Remove(key);
        }

        var root = Path.TrimEndingDirectorySeparator(repositoryRoot);
        var parent = Path.GetDirectoryName(root);
        if (!string.IsNullOrEmpty(parent))
        {
            // Discovery may look at the root itself but never climbs into its parent.
            environment["GIT_CEILING_DIRECTORIES"] = parent;
        }

        // Override core.worktree and .git/commondir from any config source.
        environment["GIT_WORK_TREE"] = root;
        environment["GIT_COMMON_DIR"] = Path.Combine(root, ".git");

        // Command-scope config: the root itself is never used as an implicit bare repository.
        environment["GIT_CONFIG_COUNT"] = "1";
        environment["GIT_CONFIG_KEY_0"] = "safe.bareRepository";
        environment["GIT_CONFIG_VALUE_0"] = "explicit";
    }

    private static async Task<SafeGitResult> RunAsync(string workingDirectory, IReadOnlyList<string> fixedArgs, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in fixedArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        ConfineToRepository(psi, workingDirectory);

        using var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true,
        };

        try
        {
            if (!process.Start())
            {
                return SafeGitResult.Failure("Failed to start git process.");
            }
        }
        catch (Exception ex)
        {
            return SafeGitResult.Failure($"Failed to start git: {ex.Message}");
        }

        var stdoutTask = ReadBoundedAsync(process.StandardOutput, MaxOutputChars, cancellationToken);
        var stderrTask = ReadBoundedAsync(process.StandardError, MaxOutputChars, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        var (stdOut, stdOutTruncated) = await stdoutTask.ConfigureAwait(false);
        var (stdErr, stdErrTruncated) = await stderrTask.ConfigureAwait(false);
        var truncated = stdOutTruncated || stdErrTruncated;

        return SafeGitResult.FromProcess(process.ExitCode, stdOut, stdErr, truncated);
    }

    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(StreamReader reader, int maxChars, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(4096, maxChars));
        var buffer = new char[4096];
        var truncated = false;
        int total = 0;

        // Not reader.EndOfStream: it blocks synchronously, so the stderr reader
        // would not start until stdout ends, and git blocks once the stderr pipe
        // is full (e.g. a usage message with no stdout) — a deadlock.
        while (true)
        {
            int read;
            try
            {
                read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            if (read == 0)
            {
                break;
            }

            if (total + read > maxChars)
            {
                var allowed = maxChars - total;
                if (allowed > 0)
                {
                    builder.Append(buffer, 0, allowed);
                }
                truncated = true;
                try
                {
                    char[] drain = new char[4096];
                    while (await reader.ReadAsync(drain, 0, drain.Length).ConfigureAwait(false) > 0) { }
                }
                catch { }
                break;
            }

            builder.Append(buffer, 0, read);
            total += read;

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return (builder.ToString(), truncated);
    }
}
