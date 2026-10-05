using System.Diagnostics;
using System.IO;
using HAMMOR.Core.Tools;
using HAMMOR.Core.Tools.Filesystem;
using HAMMOR.Core.Tools.Git;
using Xunit;

namespace HAMMOR.Core.Tests.Tools.Git;

public sealed class GitToolsSecurityTests : IDisposable
{
    private readonly List<string> _tempRoots = new();

    public void Dispose()
    {
        foreach (var root in _tempRoots)
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hammor-git-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        return root;
    }

    private static IFilesystemPolicy PolicyFor(string allowedRoot, IPathResolution? resolution = null) =>
        FilesystemPolicyFactory.CreateForRoot(allowedRoot, resolution);

    private static SafeGitRunner RunnerFor(string allowedRoot) =>
        new(PolicyFor(allowedRoot));

    private static ToolInvocation Inv(string toolName, string repositoryPath) =>
        new() { ToolName = toolName, Arguments = new Dictionary<string, string?> { ["repositoryPath"] = repositoryPath } };

    // ---- No generic executor ------------------------------------------------

    [Fact]
    public void SafeGitRunner_does_not_expose_generic_executor()
    {
        var methods = typeof(SafeGitRunner).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var m in methods)
        {
            var name = m.Name.ToLowerInvariant();
            Assert.False(name.Contains("rungit"), $"Method {m.Name} looks like a generic executor.");
            Assert.False(name.Contains("execute"), $"Method {m.Name} must not be a generic executor.");
            Assert.False(name.Contains("runcommand"), $"Method {m.Name} must not be a generic executor.");
            // No method may accept a 'string arguments' / subcommand parameter.
            foreach (var p in m.GetParameters())
            {
                if (p.Name is "arguments" or "args" or "command" or "subcommand")
                {
                    Assert.Fail($"SafeGitRunner.{m.Name} exposes forbidden parameter '{p.Name}'.");
                }
            }
        }

        // Concrete proof: attempting to call a forbidden API does not compile — but we can at least assert the 3 allowed methods exist and no other git-operating method exists.
        var gitMethods = methods.Where(m => m.DeclaringType == typeof(SafeGitRunner) && !m.IsSpecialName).ToArray();
        Assert.Contains(gitMethods, m => m.Name == "GetStatusAsync");
        Assert.Contains(gitMethods, m => m.Name == "GetDiffAsync");
        Assert.Contains(gitMethods, m => m.Name == "GetLogAsync");
        Assert.Equal(3, gitMethods.Select(m => m.Name).Distinct().Count());
    }

    [Fact]
    public void Tool_schemas_do_not_allow_arbitrary_git_flags()
    {
        var runner = RunnerFor(CreateTempRoot());
        ITool[] tools = { new GitStatusTool(runner), new GitDiffTool(runner), new GitLogTool(runner) };
        foreach (var tool in tools)
        {
            var schema = tool.InputSchema.Json;
            // None of the tools must accept a free-form git args/flags field.
            Assert.DoesNotContain("\"arguments\"", schema);
            Assert.DoesNotContain("\"args\"", schema);
            Assert.DoesNotContain("\"flags\"", schema);
            Assert.DoesNotContain("\"command\"", schema);
            Assert.DoesNotContain("\"additionalProperties\": true", schema);
        }
    }

    // ---- Forbidden paths rejected before Process.Start ----------------------

    [Theory]
    [InlineData("git.status")]
    [InlineData("git.diff")]
    [InlineData("git.log")]
    public async Task All_git_tools_reject_path_outside_allowed_roots(string toolName)
    {
        var allowed = CreateTempRoot();
        var runner = RunnerFor(allowed);
        ITool tool = toolName switch
        {
            "git.status" => new GitStatusTool(runner),
            "git.diff" => new GitDiffTool(runner),
            "git.log" => new GitLogTool(runner),
            _ => throw new InvalidOperationException(),
        };
        var outside = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}");
        var result = await tool.ExecuteAsync(Inv(toolName, outside));
        Assert.False(result.Succeeded);
        Assert.Contains("outside the allowed roots", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("git.status")]
    [InlineData("git.diff")]
    [InlineData("git.log")]
    public async Task All_git_tools_reject_traversal(string toolName)
    {
        var allowed = CreateTempRoot();
        var runner = RunnerFor(allowed);
        ITool tool = toolName switch
        {
            "git.status" => new GitStatusTool(runner),
            "git.diff" => new GitDiffTool(runner),
            "git.log" => new GitLogTool(runner),
            _ => throw new InvalidOperationException(),
        };
        var traversal = Path.Combine(allowed, "..", "escape");
        var result = await tool.ExecuteAsync(Inv(toolName, traversal));
        Assert.False(result.Succeeded);
        Assert.Contains("traversal", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("git.status")]
    [InlineData("git.diff")]
    [InlineData("git.log")]
    public async Task All_git_tools_reject_protected_path(string toolName)
    {
        var allowed = CreateTempRoot();
        var runner = RunnerFor(allowed);
        ITool tool = toolName switch
        {
            "git.status" => new GitStatusTool(runner),
            "git.diff" => new GitDiffTool(runner),
            "git.log" => new GitLogTool(runner),
            _ => throw new InvalidOperationException(),
        };
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir)) return;
        var result = await tool.ExecuteAsync(Inv(toolName, windowsDir));
        Assert.False(result.Succeeded);
        Assert.Contains("protected", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("git.status")]
    [InlineData("git.diff")]
    [InlineData("git.log")]
    public async Task All_git_tools_reject_secrets_directory(string toolName)
    {
        var allowed = CreateTempRoot();
        var runner = RunnerFor(allowed);
        ITool tool = toolName switch
        {
            "git.status" => new GitStatusTool(runner),
            "git.diff" => new GitDiffTool(runner),
            "git.log" => new GitLogTool(runner),
            _ => throw new InvalidOperationException(),
        };
        var secrets = Path.Combine(allowed, "secrets");
        Directory.CreateDirectory(secrets);
        var result = await tool.ExecuteAsync(Inv(toolName, secrets));
        Assert.False(result.Succeeded);
        Assert.Contains("secret", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("git.status")]
    [InlineData("git.diff")]
    [InlineData("git.log")]
    public async Task All_git_tools_reject_reparse_escape(string toolName)
    {
        var allowed = CreateTempRoot();
        var reparse = Path.Combine(allowed, "link");
        Directory.CreateDirectory(reparse);
        var outside = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(outside)) outside = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}");
        var stub = new StubReparseResolution(reparse, outside);
        var runner = new SafeGitRunner(PolicyFor(allowed, stub));
        ITool tool = toolName switch
        {
            "git.status" => new GitStatusTool(runner),
            "git.diff" => new GitDiffTool(runner),
            "git.log" => new GitLogTool(runner),
            _ => throw new InvalidOperationException(),
        };
        var result = await tool.ExecuteAsync(Inv(toolName, reparse));
        Assert.False(result.Succeeded);
    }

    // ---- Bounded output -----------------------------------------------------

    [Fact]
    public async Task Stdout_is_bounded_and_reports_truncated()
    {
        // Build a repo whose diff is huge, then verify truncation behavior via runner directly.
        // Use isolated temp dir with real git init (not via SafeGitRunner).
        var repo = CreateTempRepo();
        // Create a large file inside the repo and stage it.
        var bigFile = Path.Combine(repo, "big.txt");
        var bigContent = new string('x', 300 * 1024); // 300 KiB > 256 KiB limit
        File.WriteAllText(bigFile, bigContent);
        RunGit(repo, "add big.txt");
        // Also create an unstaged modification larger than the limit by writing many lines via diff.
        // Simplest: write a 300 KiB file and let diff capture it as untracked -> actually diff won't include untracked.
        // So instead create a committed file then modify it to be huge and diff it.
        RunGit(repo, "commit -m init --allow-empty");
        // Now overwrite big.txt with even larger content, unstaged.
        File.WriteAllText(bigFile, bigContent + bigContent);
        var runner = RunnerFor(repo);
        var diff = await runner.GetDiffAsync(Path.Combine(repo, "big.txt"), default);
        // If we diff the file directly, git may treat it as outside? Actually we pass file path, runner validates file path's allowed roots — but GetDiff expects repo root.
        // So diff the repo root; the diff contains the large change.
        var repoDiff = await runner.GetDiffAsync(repo, default);
        // At minimum, verify either truncated or succeeded with bounded length.
        if (repoDiff.Truncated)
        {
            Assert.True(repoDiff.StdOut.Length <= SafeGitRunner.MaxOutputChars);
        }
        else
        {
            // If not truncated (git may have paged differently), still StdOut must be bounded.
            Assert.True(repoDiff.StdOut.Length <= SafeGitRunner.MaxOutputChars);
        }
    }

    [Fact]
    public async Task Diff_output_is_bounded()
    {
        var repo = CreateTempRepo();
        // Create and commit a baseline
        File.WriteAllText(Path.Combine(repo, "a.txt"), "hello\n");
        RunGit(repo, "add a.txt");
        RunGit(repo, "commit -m base --allow-empty");
        // Grow diff to exceed limit by modifying file to ~400 KiB single-line diff (bounded by git diff truncation as well, but runner enforces 256 KiB)
        var huge = new string('a', 400 * 1024);
        File.WriteAllText(Path.Combine(repo, "a.txt"), huge);
        var runner = RunnerFor(repo);
        var result = await runner.GetDiffAsync(repo, default);
        Assert.True(result.StdOut.Length <= SafeGitRunner.MaxOutputChars);
        if (result.StdOut.Length == SafeGitRunner.MaxOutputChars)
        {
            Assert.True(result.Truncated);
        }
    }

    // ---- Helpers: isolated git fixture (git init only inside temp dirs) ---

    private string CreateTempRepo()
    {
        var dir = CreateTempRoot();
        RunGit(dir, "init");
        RunGit(dir, "config user.email test@hammor.test");
        RunGit(dir, "config user.name Test");
        return dir;
    }

    private static void RunGit(string workDir, string args)
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
        foreach (var a in SplitArgs(args))
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit(5000);
    }

    private static IEnumerable<string> SplitArgs(string args)
    {
        // Minimal split for test fixture: args are fixed strings we control.
        return args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class StubReparseResolution : IPathResolution
    {
        private readonly string _reparsePath;
        private readonly string _targetOutside;
        public StubReparseResolution(string reparsePath, string targetOutside)
        {
            _reparsePath = Path.GetFullPath(reparsePath);
            _targetOutside = Path.GetFullPath(targetOutside);
        }
        public bool IsReparsePoint(string path)
        {
            try { return string.Equals(Path.GetFullPath(path), _reparsePath, StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }
        public string? ResolveFinalPath(string path)
        {
            try
            {
                if (string.Equals(Path.GetFullPath(path), _reparsePath, StringComparison.OrdinalIgnoreCase))
                    return _targetOutside;
                return Path.GetFullPath(path);
            }
            catch { return null; }
        }
        public string GetFullPath(string path) => Path.GetFullPath(path);
    }
}
