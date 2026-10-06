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
/// directory and discovery boundary to that directory. Repository config
/// cannot make git run a program (ADR-006): the guard refuses filter drivers,
/// and fixed flags plus command-scope config switch off every other
/// config-selected program for these three commands.
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

    // --no-ext-diff / --no-textconv: diff.external, diff.<driver>.command and
    // diff.<driver>.textconv in repository config name programs (ADR-006).
    public Task<SafeGitResult> GetDiffAsync(string repositoryPath, bool staged, CancellationToken cancellationToken = default)
    {
        var args = staged
            ? new[] { "diff", "--no-color", "--ignore-submodules=all", "--no-ext-diff", "--no-textconv", "--staged" }
            : new[] { "diff", "--no-color", "--ignore-submodules=all", "--no-ext-diff", "--no-textconv" };
        return RunValidatedAsync(repositoryPath, args, cancellationToken);
    }

    public Task<SafeGitResult> GetLogAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        GetLogAsync(repositoryPath, 50, cancellationToken);

    // --no-show-signature: log.showSignature would run gpg.program or
    // gpg.ssh.program from repository config on signed commits (ADR-006).
    public Task<SafeGitResult> GetLogAsync(string repositoryPath, int maxCount, CancellationToken cancellationToken = default)
    {
        var bounded = Math.Clamp(maxCount, 1, 50);
        var countText = bounded.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return RunValidatedAsync(repositoryPath, new[] { "log", "--oneline", "-n", countText, "--no-decorate", "--no-show-signature" }, cancellationToken);
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
    /// Command-scope config (<c>GIT_CONFIG_COUNT/KEY/VALUE</c>). It outranks
    /// every config file, including the repository's, and git passes it on to
    /// any child git it starts.
    /// </summary>
    private static readonly (string Key, string Value)[] CommandScopeConfig =
    {
        // ADR-005: the root itself is never used as an implicit bare repository.
        ("safe.bareRepository", "explicit"),
        // ADR-006: core.fsmonitor=<cmd> runs a program on every index read.
        ("core.fsmonitor", "false"),
        // ADR-006: git diff otherwise rewrites .git/index after a stat refresh,
        // which fires post-index-change hooks (files or, from git 2.54, config).
        ("diff.autoRefreshIndex", "false"),
    };

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

        // ADR-006: a missing object in a partial clone is an error, never a
        // fetch through the promisor remote's upload-pack, ssh or credential helper.
        environment["GIT_NO_LAZY_FETCH"] = "1";
        // ADR-006: git status does not rewrite .git/index (no post-index-change hook).
        environment["GIT_OPTIONAL_LOCKS"] = "0";

        environment["GIT_CONFIG_COUNT"] = CommandScopeConfig.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (var i = 0; i < CommandScopeConfig.Length; i++)
        {
            var index = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            environment["GIT_CONFIG_KEY_" + index] = CommandScopeConfig[i].Key;
            environment["GIT_CONFIG_VALUE_" + index] = CommandScopeConfig[i].Value;
        }
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
