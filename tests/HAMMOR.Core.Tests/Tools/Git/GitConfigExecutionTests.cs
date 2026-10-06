using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using HAMMOR.Core.Tools;
using HAMMOR.Core.Tools.Filesystem;
using HAMMOR.Core.Tools.Git;
using Xunit;

namespace HAMMOR.Core.Tests.Tools.Git;

/// <summary>
/// ADR-006: a repository's own config must not make the read-only Git tools
/// run a program. Each test plants a payload that writes a marker file, first
/// proves with plain git that the vector is live (so a payload that cannot run
/// on this machine fails the test instead of passing it), then asserts that the
/// tool call leaves no marker.
/// </summary>
[Collection(GitProcessCollection.Name)]
public sealed class GitConfigExecutionTests : IDisposable
{
    private static int s_mtimeStep;

    private readonly List<string> _tempRoots = new();

    public static TheoryData<string> AllTools => new() { "git.status", "git.diff", "git.log" };

    public static TheoryData<string> IndexReadingTools => new() { "git.status", "git.diff" };

    public void Dispose()
    {
        foreach (var root in _tempRoots)
        {
            DeleteTree(root);
        }
    }

    // ---- core.fsmonitor ------------------------------------------------------------

    [Theory]
    [MemberData(nameof(IndexReadingTools))]
    public async Task Core_fsmonitor_hook_does_not_run(string toolName)
    {
        var allowed = CreateTempRoot();
        var repo = CreateInsideRepo(allowed);
        var payload = CreatePayload();
        Git(repo, "config", "core.fsmonitor", payload.ShellCommand);
        AssertVectorIsLive(payload, repo, PlainCommandFor(toolName));

        var result = await Tool(toolName, RunnerFor(allowed)).ExecuteAsync(Inv(toolName, repo));

        Assert.True(result.Succeeded, result.Error);
        AssertPayloadDidNotRun(payload);
    }

    // ---- diff.external, diff.<driver>.command, diff.<driver>.textconv ----------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Diff_external_does_not_run(bool staged)
    {
        var allowed = CreateTempRoot();
        var repo = CreateInsideRepo(allowed);
        var payload = CreatePayload();
        Git(repo, "config", "diff.external", payload.ShellCommand);
        File.AppendAllText(Path.Combine(repo, "inside.txt"), "INSIDE_EDIT\n");
        if (staged) Git(repo, "add", "inside.txt");
        AssertVectorIsLive(payload, repo, staged ? new[] { "diff", "--no-color", "--staged" } : new[] { "diff", "--no-color" });

        var result = await Tool("git.diff", RunnerFor(allowed)).ExecuteAsync(Inv("git.diff", repo, staged));

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("INSIDE_EDIT", result.Output);
        AssertPayloadDidNotRun(payload);
    }

    [Theory]
    [InlineData("command", false)]
    [InlineData("command", true)]
    [InlineData("textconv", false)]
    [InlineData("textconv", true)]
    public async Task Diff_driver_programs_selected_by_attributes_do_not_run(string driverKey, bool staged)
    {
        var allowed = CreateTempRoot();
        var repo = CreateInsideRepo(allowed);
        var payload = CreatePayload();
        File.WriteAllText(Path.Combine(repo, ".gitattributes"), "*.txt diff=hammor-test\n");
        Git(repo, "config", $"diff.hammor-test.{driverKey}", payload.ShellCommand);
        File.AppendAllText(Path.Combine(repo, "inside.txt"), "INSIDE_EDIT\n");
        if (staged) Git(repo, "add", "inside.txt");
        AssertVectorIsLive(payload, repo, staged ? new[] { "diff", "--no-color", "--staged" } : new[] { "diff", "--no-color" });

        var result = await Tool("git.diff", RunnerFor(allowed)).ExecuteAsync(Inv("git.diff", repo, staged));

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("INSIDE_EDIT", result.Output);
        AssertPayloadDidNotRun(payload);
    }

    // ---- filter.<driver>.clean / .process: refused --------------------------------------

    [Theory]
    [MemberData(nameof(AllTools))]
    public async Task Repository_filter_driver_is_refused(string toolName)
    {
        var allowed = CreateTempRoot();
        var repo = CreateInsideRepo(allowed);
        var payload = CreatePayload();
        File.WriteAllText(Path.Combine(repo, ".gitattributes"), "*.txt filter=hammor-test\n");
        Git(repo, "config", "filter.hammor-test.clean", payload.ShellCommand);
        MakeStatDirty(Path.Combine(repo, "inside.txt"));
        AssertVectorIsLive(payload, repo, PlainCommandFor("git.status"));
        MakeStatDirty(Path.Combine(repo, "inside.txt"));

        var result = await Tool(toolName, RunnerFor(allowed)).ExecuteAsync(Inv(toolName, repo));

        AssertRefused(result, "filter");
        AssertPayloadDidNotRun(payload);
    }

    [Theory]
    [InlineData("[filter \"hammor-test\"]\n\tprocess = {0}\n")]
    [InlineData("[FILTER \"hammor-test\"]\n\tClean = {0}\n")]
    [InlineData("[filter.hammor-test]\n\tclean = {0}\n")]
    [InlineData("[core][filter \"hammor-test\"]clean={0}\n")]
    public async Task Repository_filter_driver_variants_are_refused(string configTemplate)
    {
        var allowed = CreateTempRoot();
        var repo = CreateInsideRepo(allowed);
        var payload = CreatePayload();
        File.WriteAllText(Path.Combine(repo, ".gitattributes"), "*.txt filter=hammor-test\n");
        File.AppendAllText(Path.Combine(repo, ".git", "config"), string.Format(configTemplate, ConfigValue(payload.ShellCommand)));
        MakeStatDirty(Path.Combine(repo, "inside.txt"));
        AssertVectorIsLive(payload, repo, PlainCommandFor("git.status"));
        MakeStatDirty(Path.Combine(repo, "inside.txt"));

        var result = await Tool("git.status", RunnerFor(allowed)).ExecuteAsync(Inv("git.status", repo));

        AssertRefused(result, "filter");
        AssertPayloadDidNotRun(payload);
    }

    [Fact]
    public async Task Filter_driver_in_config_worktree_is_refused()
    {
        var allowed = CreateTempRoot();
        var repo = CreateInsideRepo(allowed);
        var payload = CreatePayload();
        Git(repo, "config", "extensions.worktreeConfig", "true");
        File.WriteAllText(Path.Combine(repo, ".gitattributes"), "*.txt filter=hammor-test\n");
        File.WriteAllText(Path.Combine(repo, ".git", "config.worktree"), $"[filter \"hammor-test\"]\n\tclean = {ConfigValue(payload.ShellCommand)}\n");
        MakeStatDirty(Path.Combine(repo, "inside.txt"));
        AssertVectorIsLive(payload, repo, PlainCommandFor("git.status"));
        MakeStatDirty(Path.Combine(repo, "inside.txt"));

        var result = await Tool("git.status", RunnerFor(allowed)).ExecuteAsync(Inv("git.status", repo));

        AssertRefused(result, "filter");
        AssertPayloadDidNotRun(payload);
    }

    [Fact]
    public async Task Filter_attribute_without_a_repository_driver_is_allowed()
    {
        // e.g. "filter=lfs" with git-lfs configured globally, not in the repository.
        var allowed = CreateTempRoot();
        var repo = CreateInsideRepo(allowed);
        File.WriteAllText(Path.Combine(repo, ".gitattributes"), "*.bin filter=lfs diff=lfs merge=lfs -text\n");

        var result = await Tool("git.status", RunnerFor(allowed)).ExecuteAsync(Inv("git.status", repo));

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains(".gitattributes", result.Output);
    }

    [Fact]
    public async Task Partial_clone_filter_setting_is_not_mistaken_for_a_filter_driver()
    {
        var allowed = CreateTempRoot();
        var repo = CreateInsideRepo(allowed);
        File.AppendAllText(Path.Combine(repo, ".git", "config"), "[remote \"origin\"]\n\tpartialclonefilter = blob:none\n");

        var result = await Tool("git.log", RunnerFor(allowed)).ExecuteAsync(Inv("git.log", repo));

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("INSIDE_COMMIT", result.Output);
    }

    // ---- log.showSignature + gpg.program / gpg.ssh.program --------------------------------

    [Theory]
    [InlineData("openpgp")]
    [InlineData("ssh")]
    public async Task Log_signature_verification_program_does_not_run(string format)
    {
        var allowed = CreateTempRoot();
        var repo = CreateInsideRepo(allowed);
        var payload = CreatePayload();
        CommitWithSignatureHeader(repo, format, "INSIDE_SIGNED");
        Git(repo, "config", "log.showSignature", "true");
        if (format == "ssh")
        {
            Git(repo, "config", "gpg.format", "ssh");
            Git(repo, "config", "gpg.ssh.program", payload.ScriptPath);
            Git(repo, "config", "gpg.ssh.allowedSignersFile", GitPath(Path.Combine(repo, "inside.txt")));
        }
        else
        {
            Git(repo, "config", "gpg.program", payload.ScriptPath);
        }
        AssertVectorIsLive(payload, repo, PlainCommandFor("git.log"));

        var result = await Tool("git.log", RunnerFor(allowed)).ExecuteAsync(Inv("git.log", repo));

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("INSIDE_SIGNED", result.Output);
        AssertPayloadDidNotRun(payload);
    }

    // ---- post-index-change hooks (fired when status/diff rewrite .git/index) ---------------

    [Theory]
    [MemberData(nameof(IndexReadingTools))]
    public async Task Post_index_change_hook_does_not_run(string toolName)
    {
        var allowed = CreateTempRoot();
        var repo = CreateInsideRepo(allowed);
        var payload = CreatePayload();
        var hook = Path.Combine(repo, ".git", "hooks", "post-index-change");
        Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
        File.Copy(payload.ScriptPath, hook);
        MakeExecutable(hook);
        MakeStatDirty(Path.Combine(repo, "inside.txt"));
        AssertVectorIsLive(payload, repo, PlainCommandFor(toolName));
        MakeStatDirty(Path.Combine(repo, "inside.txt"));

        var result = await Tool(toolName, RunnerFor(allowed)).ExecuteAsync(Inv(toolName, repo));

        Assert.True(result.Succeeded, result.Error);
        AssertPayloadDidNotRun(payload);
    }

    [Theory]
    [MemberData(nameof(IndexReadingTools))]
    public async Task Config_defined_post_index_change_hook_does_not_run(string toolName)
    {
        // hook.<name>.command / hook.<name>.event exist from git 2.54 on.
        if (InstalledGitVersion() < new Version(2, 54)) return;
        var allowed = CreateTempRoot();
        var repo = CreateInsideRepo(allowed);
        var payload = CreatePayload();
        Git(repo, "config", "hook.hammor-test.command", payload.ShellCommand);
        Git(repo, "config", "hook.hammor-test.event", "post-index-change");
        MakeStatDirty(Path.Combine(repo, "inside.txt"));
        AssertVectorIsLive(payload, repo, PlainCommandFor(toolName));
        MakeStatDirty(Path.Combine(repo, "inside.txt"));

        var result = await Tool(toolName, RunnerFor(allowed)).ExecuteAsync(Inv(toolName, repo));

        Assert.True(result.Succeeded, result.Error);
        AssertPayloadDidNotRun(payload);
    }

    // ---- Partial clone: lazy fetch from the promisor remote -------------------------------

    [Fact]
    public async Task Lazy_fetch_from_promisor_remote_does_not_run()
    {
        var server = CreateTempRoot();
        Git(server, "init", "-q");
        File.WriteAllText(Path.Combine(server, "remote.txt"), "remote\n");
        Git(server, "add", "remote.txt");
        Commit(server, "SERVER_COMMIT");
        Git(server, "config", "uploadpack.allowFilter", "true");
        var allowed = CreateTempRoot();
        Git(allowed, "clone", "-q", "--no-checkout", "--filter=blob:none", FileUrl(server), "partial");
        var repo = Path.Combine(allowed, "partial");
        // Index entries without blobs or work tree files: diff needs the missing blob.
        Git(repo, "read-tree", "HEAD");
        var payload = CreatePayload();
        Git(repo, "config", "remote.origin.uploadpack", payload.ShellCommand);
        AssertVectorIsLive(payload, repo, PlainCommandFor("git.diff"));

        var result = await Tool("git.diff", RunnerFor(allowed)).ExecuteAsync(Inv("git.diff", repo));

        Assert.DoesNotContain("is not allowed", result.Error ?? string.Empty);
        AssertPayloadDidNotRun(payload);
    }

    // ---- Regression: real changes stay visible, the index is never rewritten ---------------

    [Fact]
    public async Task Tools_still_report_changes_and_never_rewrite_the_index()
    {
        var allowed = CreateTempRoot();
        var repo = CreateInsideRepo(allowed);
        File.AppendAllText(Path.Combine(repo, "inside.txt"), "INSIDE_EDIT\n");
        File.WriteAllText(Path.Combine(repo, "staged.txt"), "INSIDE_STAGED\n");
        Git(repo, "add", "staged.txt");
        File.WriteAllText(Path.Combine(repo, "INSIDE_UNTRACKED.txt"), "x\n");
        MakeStatDirty(Path.Combine(repo, "secret.txt"));
        var index = Path.Combine(repo, ".git", "index");
        var indexBefore = File.ReadAllBytes(index);
        var runner = RunnerFor(allowed);

        var status = await Tool("git.status", runner).ExecuteAsync(Inv("git.status", repo));
        var diff = await Tool("git.diff", runner).ExecuteAsync(Inv("git.diff", repo));
        var staged = await Tool("git.diff", runner).ExecuteAsync(Inv("git.diff", repo, staged: true));
        var log = await Tool("git.log", runner).ExecuteAsync(Inv("git.log", repo));

        Assert.True(status.Succeeded, status.Error);
        Assert.Contains(" M inside.txt", status.Output);
        Assert.Contains("A  staged.txt", status.Output);
        Assert.Contains("?? INSIDE_UNTRACKED.txt", status.Output);
        Assert.DoesNotContain("secret.txt", status.Output);
        Assert.True(diff.Succeeded, diff.Error);
        Assert.Contains("+INSIDE_EDIT", diff.Output);
        Assert.DoesNotContain("secret.txt", diff.Output);
        Assert.True(staged.Succeeded, staged.Error);
        Assert.Contains("+INSIDE_STAGED", staged.Output);
        Assert.True(log.Succeeded, log.Error);
        Assert.Contains("INSIDE_COMMIT", log.Output);
        Assert.Equal(indexBefore, File.ReadAllBytes(index));
    }

    // ---- Assertions ---------------------------------------------------------------------------

    private static void AssertVectorIsLive(Payload payload, string repo, string[] plainArgs)
    {
        Git(repo, throwOnError: false, plainArgs);
        Assert.True(payload.Ran, $"Fixture problem: plain 'git {string.Join(' ', plainArgs)}' did not run the payload, so this test would prove nothing.");
        payload.Reset();
    }

    private static void AssertPayloadDidNotRun(Payload payload) =>
        Assert.False(payload.Ran, $"Repository config made git run a program: '{payload.MarkerPath}' was created.");

    private static void AssertRefused(ToolResult result, string reasonFragment)
    {
        Assert.False(result.Succeeded, $"Expected refusal, got output: {result.Output}");
        Assert.Contains("is not allowed", result.Error!, StringComparison.Ordinal);
        Assert.Contains(reasonFragment, result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Fixtures -----------------------------------------------------------------------------

    /// <summary>The tool's command as it was before ADR-006, without any hardening.</summary>
    private static string[] PlainCommandFor(string toolName) => toolName switch
    {
        "git.status" => new[] { "status", "--porcelain=v1" },
        "git.diff" => new[] { "diff", "--no-color" },
        "git.log" => new[] { "log", "--oneline", "-n", "50", "--no-decorate" },
        _ => throw new InvalidOperationException(toolName),
    };

    /// <summary>
    /// A program that writes <see cref="MarkerPath"/>, as a shell command (for
    /// config values git runs through the shell) and as a script file (for
    /// values git executes directly, and for hooks).
    /// </summary>
    private sealed class Payload
    {
        public required string MarkerPath { get; init; }
        public required string ShellCommand { get; init; }
        public required string ScriptPath { get; init; }
        public bool Ran => File.Exists(MarkerPath);
        public void Reset() => File.Delete(MarkerPath);
    }

    private Payload CreatePayload()
    {
        // Outside the repository, so the marker never shows up as an untracked file.
        var dir = CreateTempRoot();
        var marker = GitPath(Path.Combine(dir, "PAYLOAD_RAN"));
        var command = $"echo ran > \"{marker}\"";
        var script = Path.Combine(dir, "payload.sh");
        File.WriteAllText(script, $"#!/bin/sh\n{command}\n");
        MakeExecutable(script);
        return new Payload { MarkerPath = marker, ShellCommand = command, ScriptPath = GitPath(script) };
    }

    /// <summary>A quoted git config value; the payload command itself contains quotes.</summary>
    private static string ConfigValue(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>
    /// Changes only the modification time, so git must look at the content
    /// again (and, when it can, rewrite the index with the new stat data).
    /// </summary>
    private static void MakeStatDirty(string path) =>
        File.SetLastWriteTimeUtc(path, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(Interlocked.Increment(ref s_mtimeStep)));

    private static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// Adds a commit whose object carries a <c>gpgsig</c> header, so
    /// <c>log.showSignature</c> hands it to the configured verification program.
    /// </summary>
    private static void CommitWithSignatureHeader(string repo, string format, string subject)
    {
        var tree = Git(repo, "rev-parse", "HEAD^{tree}").Trim();
        var parent = Git(repo, "rev-parse", "HEAD").Trim();
        var signature = format == "ssh"
            ? "gpgsig -----BEGIN SSH SIGNATURE-----\n AAAA\n -----END SSH SIGNATURE-----\n"
            : "gpgsig -----BEGIN PGP SIGNATURE-----\n \n AAAA\n -----END PGP SIGNATURE-----\n";
        var body = $"tree {tree}\nparent {parent}\nauthor Test <test@hammor.test> 1700000000 +0000\ncommitter Test <test@hammor.test> 1700000000 +0000\n{signature}\n{subject}\n";
        var file = Path.Combine(Path.GetDirectoryName(repo)!, "signed-commit.txt");
        File.WriteAllText(file, body);
        var oid = Git(repo, "hash-object", "-t", "commit", "-w", file).Trim();
        Git(repo, "update-ref", "HEAD", oid);
    }

    private static Version InstalledGitVersion()
    {
        var match = Regex.Match(Git(Path.GetTempPath(), "--version"), @"(\d+)\.(\d+)\.(\d+)");
        return match.Success
            ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value))
            : new Version(0, 0);
    }

    private static string FileUrl(string path) => "file:///" + GitPath(path).TrimStart('/');

    private static SafeGitRunner RunnerFor(string allowedRoot) =>
        new(FilesystemPolicyFactory.CreateForRoot(allowedRoot));

    private static ITool Tool(string toolName, SafeGitRunner runner) => toolName switch
    {
        "git.status" => new GitStatusTool(runner),
        "git.diff" => new GitDiffTool(runner),
        "git.log" => new GitLogTool(runner),
        _ => throw new InvalidOperationException(toolName),
    };

    private static ToolInvocation Inv(string toolName, string repositoryPath, bool staged = false)
    {
        var arguments = new Dictionary<string, string?> { ["repositoryPath"] = repositoryPath };
        if (staged) arguments["staged"] = "true";
        return new() { ToolName = toolName, Arguments = arguments };
    }

    private string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hammor-git-exec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        return root;
    }

    /// <summary>A repository inside <paramref name="allowedRoot"/> with one commit (INSIDE_COMMIT).</summary>
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

    private static void Commit(string repo, string subject) =>
        Git(repo, "commit", "-q", "--allow-empty", "-m", subject);

    /// <summary>Git spells paths in config files with forward slashes.</summary>
    private static string GitPath(string path) => path.Replace('\\', '/');

    private static string Git(string workDir, params string[] args) => Git(workDir, throwOnError: true, args);

    private static string Git(string workDir, bool throwOnError, params string[] args)
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
        if (throwOnError && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({process.ExitCode}): {stderr.Result}");
        }
        return stdout.Result;
    }

    /// <summary>Deletes a fixture tree; git marks object files read-only.</summary>
    private static void DeleteTree(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
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
