using System.Diagnostics;
using System.IO;
using HAMMOR.Core.Tools;
using HAMMOR.Core.Tools.Filesystem;
using HAMMOR.Core.Tools.Git;
using Xunit;

namespace HAMMOR.Core.Tests.Tools.Git;

/// <summary>
/// Tests that spawn real git processes share one collection so they never run
/// in parallel with each other (one test changes the process environment).
/// </summary>
[CollectionDefinition(Name)]
public sealed class GitProcessCollection
{
    public const string Name = "Git process";
}

/// <summary>
/// ADR-005: a repository's own layout or config must not send the interactive
/// Git tools outside the directory that passed <see cref="IFilesystemPolicy"/>.
/// Every fixture puts the redirection target in a temp root that is NOT an
/// allowed root and fills it with OUTSIDE_* markers that must never appear in
/// tool output.
/// </summary>
[Collection(GitProcessCollection.Name)]
public sealed class GitRepositoryConfinementTests : IDisposable
{
    private const string OutsideMarker = "OUTSIDE_";

    private readonly List<string> _tempRoots = new();

    public static TheoryData<string> AllTools => new() { "git.status", "git.diff", "git.log" };

    public void Dispose()
    {
        foreach (var root in _tempRoots)
        {
            DeleteTree(root);
        }
    }

    // ---- Regression: ordinary repositories keep working --------------------

    [Fact]
    public async Task Ordinary_repository_still_works_for_all_tools()
    {
        var allowed = CreateTempRoot();
        var repo = CreateInsideRepo(allowed);
        File.AppendAllText(Path.Combine(repo, "inside.txt"), "INSIDE_EDIT\n");
        File.WriteAllText(Path.Combine(repo, "INSIDE_UNTRACKED.txt"), "x\n");
        var runner = RunnerFor(allowed);

        var status = await Tool("git.status", runner).ExecuteAsync(Inv("git.status", repo));
        var diff = await Tool("git.diff", runner).ExecuteAsync(Inv("git.diff", repo));
        var log = await Tool("git.log", runner).ExecuteAsync(Inv("git.log", repo));

        Assert.True(status.Succeeded, status.Error);
        Assert.Contains("INSIDE_UNTRACKED.txt", status.Output);
        Assert.True(diff.Succeeded, diff.Error);
        Assert.Contains("INSIDE_EDIT", diff.Output);
        Assert.True(log.Succeeded, log.Error);
        Assert.Contains("INSIDE_COMMIT", log.Output);
    }

    [Fact]
    public async Task Harmless_config_worktree_file_is_allowed()
    {
        var allowed = CreateTempRoot();
        var repo = CreateInsideRepo(allowed);
        Git(repo, "config", "extensions.worktreeConfig", "true");
        File.WriteAllText(Path.Combine(repo, ".git", "config.worktree"), "[core]\n\tsparseCheckout = false\n");

        var result = await Tool("git.log", RunnerFor(allowed)).ExecuteAsync(Inv("git.log", repo));

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("INSIDE_COMMIT", result.Output);
    }

    // ---- .git file (gitdir: indirection) ------------------------------------

    [Theory]
    [MemberData(nameof(AllTools))]
    public async Task Dot_git_file_is_refused(string toolName)
    {
        var allowed = CreateTempRoot();
        var outside = CreateOutsideRepo();
        var repo = Directory.CreateDirectory(Path.Combine(allowed, "linked")).FullName;
        File.WriteAllText(Path.Combine(repo, ".git"), $"gitdir: {GitPath(Path.Combine(outside, ".git"))}\n");

        var result = await Tool(toolName, RunnerFor(allowed)).ExecuteAsync(Inv(toolName, repo));

        AssertRefusedWithoutLeak(result, "gitdir");
    }

    // ---- Parent-directory discovery ------------------------------------------

    [Theory]
    [MemberData(nameof(AllTools))]
    public async Task Parent_repository_discovery_is_refused(string toolName)
    {
        var outside = CreateOutsideRepo();
        var allowed = Directory.CreateDirectory(Path.Combine(outside, "inner")).FullName;

        var result = await Tool(toolName, RunnerFor(allowed)).ExecuteAsync(Inv(toolName, allowed));

        AssertRefusedWithoutLeak(result, "parent-directory discovery");
    }

    [Theory]
    [MemberData(nameof(AllTools))]
    public async Task Invalid_dot_git_directory_does_not_fall_through_to_parent_repository(string toolName)
    {
        var outside = CreateOutsideRepo();
        var allowed = Directory.CreateDirectory(Path.Combine(outside, "inner")).FullName;
        Directory.CreateDirectory(Path.Combine(allowed, ".git"));

        var result = await Tool(toolName, RunnerFor(allowed)).ExecuteAsync(Inv(toolName, allowed));

        Assert.False(result.Succeeded);
        AssertNoLeak(result);
    }

    [Theory]
    [MemberData(nameof(AllTools))]
    public async Task Repository_directory_is_not_used_as_an_implicit_bare_repository(string toolName)
    {
        var allowed = CreateTempRoot();
        var outside = CreateOutsideRepo();
        var repo = Path.Combine(allowed, "bare-shaped");
        Git(allowed, "init", "--bare", "bare-shaped");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        File.WriteAllText(Path.Combine(repo, "objects", "info", "alternates"), GitPath(Path.Combine(outside, ".git", "objects")) + "\n");
        File.WriteAllText(Path.Combine(repo, "HEAD"), HeadOf(outside) + "\n");

        var result = await Tool(toolName, RunnerFor(allowed)).ExecuteAsync(Inv(toolName, repo));

        Assert.False(result.Succeeded);
        AssertNoLeak(result);
    }

    // ---- core.worktree ---------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllTools))]
    public async Task Core_worktree_is_refused(string toolName)
    {
        var allowed = CreateTempRoot();
        var outside = CreateOutsideRepo();
        var repo = CreateInsideRepo(allowed);
        File.AppendAllText(Path.Combine(repo, ".git", "config"), $"[core]\n\tworktree = {GitPath(outside)}\n");

        var result = await Tool(toolName, RunnerFor(allowed)).ExecuteAsync(Inv(toolName, repo));

        AssertRefusedWithoutLeak(result, "worktree");
    }

    [Theory]
    [InlineData("[CORE] WorkTree = {0}\n")]
    [InlineData("[core]worktree={0}\n")]
    [InlineData("[Core]\n\tWORKTREE   =   \"{0}\"\n")]
    public async Task Core_worktree_header_line_and_case_variants_are_refused(string configTemplate)
    {
        var allowed = CreateTempRoot();
        var outside = CreateOutsideRepo();
        var repo = CreateInsideRepo(allowed);
        File.AppendAllText(Path.Combine(repo, ".git", "config"), string.Format(configTemplate, GitPath(outside)));

        var result = await Tool("git.status", RunnerFor(allowed)).ExecuteAsync(Inv("git.status", repo));

        AssertRefusedWithoutLeak(result, "worktree");
    }

    // ---- [include] / [includeIf] ------------------------------------------------

    [Theory]
    [MemberData(nameof(AllTools))]
    public async Task Config_include_is_refused(string toolName)
    {
        var allowed = CreateTempRoot();
        var outside = CreateOutsideRepo();
        var included = WriteOutsideConfig(outside);
        var repo = CreateInsideRepo(allowed);
        File.AppendAllText(Path.Combine(repo, ".git", "config"), $"[include]\n\tpath = {GitPath(included)}\n");

        var result = await Tool(toolName, RunnerFor(allowed)).ExecuteAsync(Inv(toolName, repo));

        AssertRefusedWithoutLeak(result, "include");
    }

    [Theory]
    [InlineData("[includeIf \"gitdir:/\"]\n\tpath = {0}\n")]
    [InlineData("[INCLUDE]\n\tPath = {0}\n")]
    [InlineData("[core][include] path = {0}\n")]
    [InlineData("[includeif \"onbranch:*\"] path = {0}\n")]
    public async Task Config_include_variants_are_refused(string configTemplate)
    {
        var allowed = CreateTempRoot();
        var outside = CreateOutsideRepo();
        var included = WriteOutsideConfig(outside);
        var repo = CreateInsideRepo(allowed);
        File.AppendAllText(Path.Combine(repo, ".git", "config"), string.Format(configTemplate, GitPath(included)));

        var result = await Tool("git.status", RunnerFor(allowed)).ExecuteAsync(Inv("git.status", repo));

        AssertRefusedWithoutLeak(result, "include");
    }

    // ---- commondir ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllTools))]
    public async Task Commondir_is_refused(string toolName)
    {
        var allowed = CreateTempRoot();
        var outside = CreateOutsideRepo();
        var repo = CreateInsideRepo(allowed);
        File.WriteAllText(Path.Combine(repo, ".git", "commondir"), GitPath(Path.Combine(outside, ".git")) + "\n");

        var result = await Tool(toolName, RunnerFor(allowed)).ExecuteAsync(Inv(toolName, repo));

        AssertRefusedWithoutLeak(result, "commondir");
    }

    // ---- config.worktree ------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllTools))]
    public async Task Config_worktree_setting_core_worktree_is_refused(string toolName)
    {
        var allowed = CreateTempRoot();
        var outside = CreateOutsideRepo();
        var repo = CreateInsideRepo(allowed);
        Git(repo, "config", "extensions.worktreeConfig", "true");
        File.WriteAllText(Path.Combine(repo, ".git", "config.worktree"), $"[core]\n\tworktree = {GitPath(outside)}\n");

        var result = await Tool(toolName, RunnerFor(allowed)).ExecuteAsync(Inv(toolName, repo));

        AssertRefusedWithoutLeak(result, "config.worktree");
    }

    [Fact]
    public async Task Config_worktree_include_is_refused()
    {
        var allowed = CreateTempRoot();
        var outside = CreateOutsideRepo();
        var included = WriteOutsideConfig(outside);
        var repo = CreateInsideRepo(allowed);
        Git(repo, "config", "extensions.worktreeConfig", "true");
        File.WriteAllText(Path.Combine(repo, ".git", "config.worktree"), $"[include]\n\tpath = {GitPath(included)}\n");

        var result = await Tool("git.status", RunnerFor(allowed)).ExecuteAsync(Inv("git.status", repo));

        AssertRefusedWithoutLeak(result, "include");
    }

    // ---- Object alternates ------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllTools))]
    public async Task Object_alternates_are_refused(string toolName)
    {
        var allowed = CreateTempRoot();
        var outside = CreateOutsideRepo();
        var repo = CreateInsideRepo(allowed);
        File.WriteAllText(Path.Combine(repo, ".git", "objects", "info", "alternates"), GitPath(Path.Combine(outside, ".git", "objects")) + "\n");
        File.WriteAllText(Path.Combine(repo, ".git", "HEAD"), HeadOf(outside) + "\n");

        var result = await Tool(toolName, RunnerFor(allowed)).ExecuteAsync(Inv(toolName, repo));

        AssertRefusedWithoutLeak(result, "alternates");
    }

    // ---- Reparse points inside the repository's git directory ---------------------------

    [Theory]
    [MemberData(nameof(AllTools))]
    public async Task Dot_git_junction_is_refused(string toolName)
    {
        if (!OperatingSystem.IsWindows()) return;
        var allowed = CreateTempRoot();
        var outside = CreateOutsideRepo();
        var repo = Directory.CreateDirectory(Path.Combine(allowed, "junctioned")).FullName;
        CreateJunction(Path.Combine(repo, ".git"), Path.Combine(outside, ".git"));

        var result = await Tool(toolName, RunnerFor(allowed)).ExecuteAsync(Inv(toolName, repo));

        AssertRefusedWithoutLeak(result, "reparse");
    }

    [Fact]
    public async Task Junction_directly_inside_dot_git_is_refused()
    {
        if (!OperatingSystem.IsWindows()) return;
        var allowed = CreateTempRoot();
        var outside = CreateOutsideRepo();
        var repo = CreateInsideRepo(allowed);
        var objects = Path.Combine(repo, ".git", "objects");
        DeleteTree(objects);
        CreateJunction(objects, Path.Combine(outside, ".git", "objects"));
        File.WriteAllText(Path.Combine(repo, ".git", "HEAD"), HeadOf(outside) + "\n");

        var result = await Tool("git.log", RunnerFor(allowed)).ExecuteAsync(Inv("git.log", repo));

        AssertRefusedWithoutLeak(result, "reparse");
    }

    // ---- Submodule checkouts whose .git file points outside ------------------------------

    [Fact]
    public async Task Submodule_gitfile_pointing_outside_is_not_followed()
    {
        var allowed = CreateTempRoot();
        var outside = CreateOutsideRepo();
        var repo = CreateInsideRepo(allowed);
        var recorded = HeadOf(outside);
        Git(repo, "update-index", "--add", "--cacheinfo", $"160000,{recorded},sub");
        Commit(repo, "INSIDE_ADD_SUBMODULE");
        Git(repo, "config", "diff.submodule", "log");
        Directory.CreateDirectory(Path.Combine(repo, "sub"));
        File.WriteAllText(Path.Combine(repo, "sub", ".git"), $"gitdir: {GitPath(Path.Combine(outside, ".git"))}\n");
        File.WriteAllText(Path.Combine(outside, "second.txt"), "x\n");
        Git(outside, "add", "second.txt");
        Commit(outside, "OUTSIDE_SECOND_SUBJECT");
        var runner = RunnerFor(allowed);

        var diff = await Tool("git.diff", runner).ExecuteAsync(Inv("git.diff", repo));
        var status = await Tool("git.status", runner).ExecuteAsync(Inv("git.status", repo));

        Assert.True(diff.Succeeded, diff.Error);
        AssertNoLeak(diff);
        Assert.True(status.Succeeded, status.Error);
        Assert.DoesNotContain("sub", status.Output);
    }

    // ---- Inherited GIT_* environment ---------------------------------------------------------

    [Fact]
    public async Task Inherited_git_environment_variables_are_not_passed_to_git()
    {
        var allowed = CreateTempRoot();
        var outside = CreateOutsideRepo();
        var repo = CreateInsideRepo(allowed);
        File.WriteAllText(Path.Combine(repo, ".git", "HEAD"), HeadOf(outside) + "\n");
        const string variable = "GIT_ALTERNATE_OBJECT_DIRECTORIES";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, Path.Combine(outside, ".git", "objects"));
        ToolResult result;
        try
        {
            result = await Tool("git.log", RunnerFor(allowed)).ExecuteAsync(Inv("git.log", repo));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }

        Assert.False(result.Succeeded);
        AssertNoLeak(result);
    }

    // ---- Assertions ------------------------------------------------------------------------------

    private static void AssertRefusedWithoutLeak(ToolResult result, string reasonFragment)
    {
        Assert.False(result.Succeeded, $"Expected refusal, got output: {result.Output}");
        Assert.Contains(reasonFragment, result.Error!, StringComparison.OrdinalIgnoreCase);
        AssertNoLeak(result);
    }

    private static void AssertNoLeak(ToolResult result)
    {
        Assert.DoesNotContain(OutsideMarker, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(OutsideMarker, result.Error ?? string.Empty, StringComparison.Ordinal);
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    private static SafeGitRunner RunnerFor(string allowedRoot) =>
        new(FilesystemPolicyFactory.CreateForRoot(allowedRoot));

    private static ITool Tool(string toolName, SafeGitRunner runner) => toolName switch
    {
        "git.status" => new GitStatusTool(runner),
        "git.diff" => new GitDiffTool(runner),
        "git.log" => new GitLogTool(runner),
        _ => throw new InvalidOperationException(toolName),
    };

    private static ToolInvocation Inv(string toolName, string repositoryPath) =>
        new() { ToolName = toolName, Arguments = new Dictionary<string, string?> { ["repositoryPath"] = repositoryPath } };

    private string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hammor-git-confine-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        return root;
    }

    /// <summary>
    /// A repository outside every allowed root: one commit (OUTSIDE_COMMIT_SUBJECT),
    /// an untracked file and an unstaged change, all carrying the outside marker.
    /// </summary>
    private string CreateOutsideRepo()
    {
        var root = CreateTempRoot();
        Git(root, "init", "-q");
        File.WriteAllText(Path.Combine(root, "secret.txt"), "OUTSIDE_SECRET\n");
        Git(root, "add", "secret.txt");
        Commit(root, "OUTSIDE_COMMIT_SUBJECT");
        File.AppendAllText(Path.Combine(root, "secret.txt"), "OUTSIDE_MODIFIED\n");
        File.WriteAllText(Path.Combine(root, "OUTSIDE_UNTRACKED.txt"), "x\n");
        return root;
    }

    /// <summary>
    /// A repository inside <paramref name="allowedRoot"/>. It also tracks a
    /// <c>secret.txt</c>, so a redirected work tree would diff the outside copy.
    /// </summary>
    private static string CreateInsideRepo(string allowedRoot)
    {
        var repo = Directory.CreateDirectory(Path.Combine(allowedRoot, "repo")).FullName;
        Git(repo, "init", "-q");
        File.WriteAllText(Path.Combine(repo, "inside.txt"), "inside\n");
        File.WriteAllText(Path.Combine(repo, "secret.txt"), "inside version\n");
        Git(repo, "add", "inside.txt", "secret.txt");
        Commit(repo, "INSIDE_COMMIT");
        return repo;
    }

    private static string WriteOutsideConfig(string outside)
    {
        var path = Path.Combine(outside, "included.cfg");
        File.WriteAllText(path, "[status]\n\tshowUntrackedFiles = all\n");
        return path;
    }

    private static string HeadOf(string repo) => Git(repo, "rev-parse", "HEAD").Trim();

    private static void Commit(string repo, string subject) =>
        Git(repo, "commit", "-q", "--allow-empty", "-m", subject);

    /// <summary>Git spells paths in config files with forward slashes.</summary>
    private static string GitPath(string path) => path.Replace('\\', '/');

    private static string Git(string workDir, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var setting in new[] { "user.email=test@hammor.test", "user.name=Test", "core.autocrlf=false", "commit.gpgsign=false" })
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(setting);
        }
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException($"git {string.Join(' ', args)} timed out.");
        }
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({process.ExitCode}): {stderr.Result}");
        }
        return stdout.Result;
    }

    private static void CreateJunction(string link, string target)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add("mklink");
        psi.ArgumentList.Add("/J");
        psi.ArgumentList.Add(link);
        psi.ArgumentList.Add(target);
        using var process = Process.Start(psi)!;
        process.WaitForExit(10_000);
        if (process.ExitCode != 0 || !Directory.Exists(link))
        {
            throw new InvalidOperationException($"mklink /J failed: {process.StandardError.ReadToEnd()}");
        }
    }

    /// <summary>
    /// Deletes a fixture tree. Junctions are removed as links first, so their
    /// targets are never touched; git marks object files read-only.
    /// </summary>
    private static void DeleteTree(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
            var junctions = Directory.EnumerateDirectories(root, "*", options)
                .Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0)
                .ToList();
            foreach (var junction in junctions)
            {
                Directory.Delete(junction);
            }
            options.AttributesToSkip = FileAttributes.ReparsePoint;
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(root, recursive: true);
        }
        catch
        {
        }
    }
}
