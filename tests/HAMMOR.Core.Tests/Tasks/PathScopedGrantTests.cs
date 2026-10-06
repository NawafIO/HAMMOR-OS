using HAMMOR.Core.Memory;
using HAMMOR.Core.Tasks;
using HAMMOR.Core.Tests.TestDoubles;
using HAMMOR.Core.Tools;
using HAMMOR.Core.Tools.Filesystem;
using HAMMOR.Core.Tools.Git;
using HAMMOR.Core.Tools.Project;
using HAMMOR.Infrastructure.Tools;
using Xunit;

namespace HAMMOR.Core.Tests.Tasks;

/// <summary>
/// Stub resolution that treats chosen directories as reparse points, optionally
/// redirecting them, so link behaviour is testable without creating real links.
/// </summary>
internal sealed class MappedReparseResolution : IPathResolution
{
    private readonly Dictionary<string, string?> _links = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="target">Where the link points; null marks a reparse point that resolves to itself.</param>
    public MappedReparseResolution Link(string path, string? target = null)
    {
        _links[Path.GetFullPath(path)] = target is null ? null : Path.GetFullPath(target);
        return this;
    }

    public bool IsReparsePoint(string path)
    {
        try
        {
            return _links.ContainsKey(Path.GetFullPath(path));
        }
        catch
        {
            return false;
        }
    }

    public string? ResolveFinalPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return _links.TryGetValue(full, out var target) ? target ?? full : full;
        }
        catch
        {
            return null;
        }
    }

    public string GetFullPath(string path) => Path.GetFullPath(path);
}

internal sealed class EmptyMemoryStore : IMemoryStore
{
    public Task<MemoryEntry> SaveAsync(MemoryEntry entry, CancellationToken cancellationToken = default) => Task.FromResult(entry);

    public Task<MemoryEntry?> GetAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<MemoryEntry?>(null);

    public Task<IReadOnlyList<MemoryEntry>> SearchAsync(string q, int limit = 10, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MemoryEntry>>([]);

    public Task<IReadOnlyList<MemoryEntry>> GetRecentAsync(string? projectId = null, int limit = 50, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MemoryEntry>>([]);

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>ADR-004 §2: path-scoped grants, checked on the policy-resolved path.</summary>
public sealed class PathScopedGrantTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hammor-scope-{Guid.NewGuid():N}");
    private readonly TestClock _clock = new(TestTasks.Start);

    public PathScopedGrantTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Granted);
        Directory.CreateDirectory(Path.Combine(Granted, "inner"));
        Directory.CreateDirectory(Evil);
        Directory.CreateDirectory(Other);
    }

    private string Granted => Path.Combine(_root, "granted");

    private string Evil => Path.Combine(_root, "granted-evil");

    private string Other => Path.Combine(_root, "other");

    private (TaskPathScope Scope, IFilesystemPolicy Policy) Build(IPathResolution? resolution = null)
    {
        resolution ??= new ManagedPathResolution();
        var policy = FilesystemPolicyFactory.CreateForRoot(_root, resolution);
        return (new TaskPathScope(policy, resolution), policy);
    }

    private static ToolInvocation Call(ITool tool, string argument, string? value) => new()
    {
        ToolName = tool.Name,
        Arguments = new Dictionary<string, string?> { [argument] = value },
    };

    private IReadOnlyList<string> GrantedRoots(TaskPathScope scope)
    {
        var roots = scope.ValidateRoots([Granted]);
        Assert.True(roots.IsValid, string.Join(" ", roots.Errors));
        return roots.CanonicalRoots;
    }

    // ------------------------------------------------------------ run time

    [Fact]
    public void Path_inside_a_granted_root_is_allowed()
    {
        var (scope, policy) = Build();
        var roots = GrantedRoots(scope);

        var read = new ReadFileTool(policy);
        var list = new ListDirectoryTool(policy);

        Assert.Equal(ToolGateOutcome.Allow, scope.CheckCall(read, Call(read, "path", Path.Combine(Granted, "inner", "a.txt")), roots).Outcome);
        Assert.Equal(ToolGateOutcome.Allow, scope.CheckCall(list, Call(list, "path", Granted), roots).Outcome);
        Assert.Equal(ToolGateOutcome.Allow, scope.CheckCall(list, Call(list, "path", Granted + Path.DirectorySeparatorChar), roots).Outcome);
    }

    [Fact]
    public void Sibling_directory_sharing_the_root_prefix_is_blocked()
    {
        var (scope, policy) = Build();
        var read = new ReadFileTool(policy);

        var decision = scope.CheckCall(read, Call(read, "path", Path.Combine(Evil, "a.txt")), GrantedRoots(scope));

        Assert.Equal(ToolGateOutcome.Block, decision.Outcome);
        Assert.Contains("outside the task's granted roots", decision.Reason);
    }

    [Fact]
    public void Path_elsewhere_inside_the_global_policy_is_still_blocked()
    {
        var (scope, policy) = Build();
        var read = new ReadFileTool(policy);

        Assert.True(policy.Validate(Path.Combine(Other, "a.txt")).IsAllowed); // the global policy would allow it
        Assert.Equal(
            ToolGateOutcome.Block,
            scope.CheckCall(read, Call(read, "path", Path.Combine(Other, "a.txt")), GrantedRoots(scope)).Outcome);
    }

    [Fact]
    public void Case_differences_resolve_like_the_policy()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Windows paths are case-insensitive; the comparison mirrors the policy there.
        }

        var (scope, policy) = Build();
        var read = new ReadFileTool(policy);
        var upper = Path.Combine(_root, "GRANTED", "a.txt");

        Assert.Equal(ToolGateOutcome.Allow, scope.CheckCall(read, Call(read, "path", upper), GrantedRoots(scope)).Outcome);
    }

    [Fact]
    public void Traversal_relative_extended_and_unc_forms_never_escape_the_root()
    {
        var (scope, policy) = Build();
        var read = new ReadFileTool(policy);
        var roots = GrantedRoots(scope);

        var attempts = new[]
        {
            Granted + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "other" + Path.DirectorySeparatorChar + "a.txt",
            "a.txt",
            @"\\?\" + Path.Combine(Other, "a.txt"),
            @"\\localhost\share\a.txt",
        };

        foreach (var attempt in attempts)
        {
            Assert.Equal(ToolGateOutcome.Block, scope.CheckCall(read, Call(read, "path", attempt), roots).Outcome);
        }
    }

    [Fact]
    public void Link_inside_a_granted_root_pointing_elsewhere_is_blocked_on_the_resolved_path()
    {
        var link = Path.Combine(Granted, "link");
        Directory.CreateDirectory(link);
        var resolution = new MappedReparseResolution().Link(link, Other);
        var (scope, policy) = Build(resolution);
        var read = new ReadFileTool(policy);
        var target = Path.Combine(link, "a.txt");

        // The global policy allows it (the link stays inside the global root)...
        Assert.True(policy.Validate(target).IsAllowed);

        // ...but the grant check sees where it really goes.
        var decision = scope.CheckCall(read, Call(read, "path", target), GrantedRoots(scope));
        Assert.Equal(ToolGateOutcome.Block, decision.Outcome);
        Assert.Contains("outside the task's granted roots", decision.Reason);
    }

    [Fact]
    public void Missing_or_blank_path_argument_is_blocked()
    {
        var (scope, policy) = Build();
        var read = new ReadFileTool(policy);
        var roots = GrantedRoots(scope);

        Assert.Equal(ToolGateOutcome.Block, scope.CheckCall(read, new ToolInvocation { ToolName = read.Name }, roots).Outcome);
        Assert.Equal(ToolGateOutcome.Block, scope.CheckCall(read, Call(read, "path", "  "), roots).Outcome);
    }

    [Fact]
    public void Path_scoped_tool_without_roots_is_rejected_at_creation_and_blocked_at_run_time()
    {
        var (scope, policy) = Build();
        var read = new ReadFileTool(policy);
        var registry = new ToolRegistry();
        registry.Register(read);

        var grant = TestTasks.Grant("t", _clock, [read.Name]);
        Assert.Contains(
            TaskGrantValidator.Validate(grant, registry, _clock.GetUtcNow()),
            e => e.Contains("at least one root", StringComparison.Ordinal));

        // A Phase 5-style grant (no roots) that reaches the gate anyway.
        var context = new UnattendedRunContext("t", grant, _clock, scope);
        Assert.Equal(
            ToolGateOutcome.Block,
            context.Check(read, Call(read, "path", Path.Combine(Granted, "a.txt"))).Outcome);
    }

    [Fact]
    public void Run_context_without_a_path_scope_blocks_path_tools()
    {
        var (scope, policy) = Build();
        var read = new ReadFileTool(policy);
        var grant = TestTasks.Grant("t", _clock, [read.Name]) with { AllowedRoots = GrantedRoots(scope) };

        var context = new UnattendedRunContext("t", grant, _clock, pathScope: null);

        Assert.Equal(
            ToolGateOutcome.Block,
            context.Check(read, Call(read, "path", Path.Combine(Granted, "a.txt"))).Outcome);
    }

    [Fact]
    public void Run_context_with_roots_allows_inside_and_blocks_outside()
    {
        var (scope, policy) = Build();
        var read = new ReadFileTool(policy);
        var grant = TestTasks.Grant("t", _clock, [read.Name]) with { AllowedRoots = GrantedRoots(scope) };
        var context = new UnattendedRunContext("t", grant, _clock, scope);

        Assert.Equal(ToolGateOutcome.Allow, context.Check(read, Call(read, "path", Path.Combine(Granted, "a.txt"))).Outcome);
        Assert.Equal(ToolGateOutcome.Block, context.Check(read, Call(read, "path", Path.Combine(Other, "a.txt"))).Outcome);
        Assert.Equal(1, context.ToolCallsAuthorised); // a blocked call is never counted as authorised
    }

    [Fact]
    public void Project_inspect_is_path_scoped()
    {
        var (scope, policy) = Build();
        var inspect = new ProjectInspectTool(policy);
        var roots = GrantedRoots(scope);

        Assert.Equal(ToolGateOutcome.Allow, scope.CheckCall(inspect, Call(inspect, "projectPath", Granted), roots).Outcome);
        Assert.Equal(ToolGateOutcome.Block, scope.CheckCall(inspect, Call(inspect, "projectPath", Other), roots).Outcome);
    }

    [Fact]
    public void Memory_search_is_not_path_scoped_and_ignores_roots()
    {
        var (scope, _) = Build();
        var search = new SearchMemoryTool(new EmptyMemoryStore());
        var grant = TestTasks.Grant("t", _clock, [search.Name]);
        var context = new UnattendedRunContext("t", grant, _clock, scope);

        Assert.False(((ITool)search) is IPathScopedTool);
        Assert.Equal(ToolGateOutcome.Allow, scope.CheckCall(search, new ToolInvocation { ToolName = search.Name }, []).Outcome);
        Assert.Equal(ToolGateOutcome.Allow, context.Check(search, new ToolInvocation { ToolName = search.Name }).Outcome);
    }

    [Fact]
    public void Every_registered_filesystem_git_and_project_tool_declares_its_path_argument()
    {
        var (_, policy) = Build();
        var runner = new SafeGitRunner(policy);
        var tools = new ITool[]
        {
            new ListDirectoryTool(policy), new ReadFileTool(policy), new WriteFileTool(policy), new DeleteFileTool(policy),
            new GitStatusTool(runner), new GitDiffTool(runner), new GitLogTool(runner), new ProjectInspectTool(policy),
        };

        foreach (var tool in tools)
        {
            var scoped = Assert.IsAssignableFrom<IPathScopedTool>(tool);
            Assert.NotEmpty(scoped.PathArguments);
            foreach (var argument in scoped.PathArguments)
            {
                Assert.Contains($"\"{argument}\"", tool.InputSchema.Json, StringComparison.Ordinal);
            }
        }

        Assert.All(tools.Where(t => t.Name.StartsWith("git.", StringComparison.Ordinal)), t => Assert.IsAssignableFrom<IGitRepositoryScopedTool>(t));
    }

    // ------------------------------------------------------------ Git rule

    [Fact]
    public void Git_requires_a_real_git_directory_inside_the_root()
    {
        var repo = Path.Combine(Granted, "repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Directory.CreateDirectory(Path.Combine(repo, "src"));

        var fileRepo = Path.Combine(Granted, "worktree");
        Directory.CreateDirectory(fileRepo);
        File.WriteAllText(Path.Combine(fileRepo, ".git"), "gitdir: C:/elsewhere/.git/worktrees/x");

        var linkedRepo = Path.Combine(Granted, "linked");
        Directory.CreateDirectory(Path.Combine(linkedRepo, ".git"));

        var resolution = new MappedReparseResolution().Link(Path.Combine(linkedRepo, ".git"));
        var (scope, policy) = Build(resolution);
        var status = new GitStatusTool(new SafeGitRunner(policy));
        var roots = GrantedRoots(scope);

        Assert.Equal(ToolGateOutcome.Allow, scope.CheckCall(status, Call(status, "repositoryPath", repo), roots).Outcome);

        // Subdirectory: Git would discover the parent repository.
        Assert.Equal(ToolGateOutcome.Block, scope.CheckCall(status, Call(status, "repositoryPath", Path.Combine(repo, "src")), roots).Outcome);

        // .git file (gitdir: indirection).
        Assert.Equal(ToolGateOutcome.Block, scope.CheckCall(status, Call(status, "repositoryPath", fileRepo), roots).Outcome);

        // .git that is a reparse point.
        Assert.Equal(ToolGateOutcome.Block, scope.CheckCall(status, Call(status, "repositoryPath", linkedRepo), roots).Outcome);
    }

    [Fact]
    public void Git_config_that_points_git_outside_the_repository_is_blocked()
    {
        var (scope, policy) = Build();
        var status = new GitStatusTool(new SafeGitRunner(policy));
        var roots = GrantedRoots(scope);

        string Repo(string name, string? config = null, string? extraFile = null, string extraContent = "elsewhere")
        {
            var repo = Path.Combine(Granted, name);
            var git = Directory.CreateDirectory(Path.Combine(repo, ".git")).FullName;
            if (config is not null)
            {
                File.WriteAllText(Path.Combine(git, "config"), config);
            }

            if (extraFile is not null)
            {
                File.WriteAllText(Path.Combine(git, extraFile), extraContent);
            }

            return repo;
        }

        var blocked = new[]
        {
            Repo("wt1", "[core]\n\tworktree = " + Other),
            Repo("wt2", "[core] worktree = " + Other),            // Git accepts a key on the header line
            Repo("wt3", "[CORE]\n\tWorkTree=" + Other),          // keys and sections are case-insensitive
            Repo("inc1", "[include]\n\tpath = ../../other.cfg"),
            Repo("inc2", "[includeIf \"gitdir:/x/\"]\n\tpath = y.cfg"),
            Repo("common", extraFile: "commondir"),
            Repo("perwt", extraFile: "config.worktree", extraContent: "[core]\n\tworktree = " + Other),
        };

        foreach (var repo in blocked)
        {
            Assert.Equal(ToolGateOutcome.Block, scope.CheckCall(status, Call(status, "repositoryPath", repo), roots).Outcome);
        }

        // An ordinary repository config, including names that merely contain "worktree", is allowed.
        // (GitRepositoryGuard is deliberately coarse: "worktree =" anywhere, even in a comment, is refused.)
        var normal = Repo(
            "normal",
            "[core]\n\trepositoryformatversion = 0\n\tbare = false\n[branch \"worktree-feature\"]\n\tremote = origin\n"
            + "[extensions]\n\tworktreeConfig = false\n");
        Assert.Equal(ToolGateOutcome.Allow, scope.CheckCall(status, Call(status, "repositoryPath", normal), roots).Outcome);
    }

    // ------------------------------------------------------------ roots at creation

    [Fact]
    public void Valid_roots_are_canonicalised_with_a_trailing_separator()
    {
        var (scope, _) = Build();

        var result = scope.ValidateRoots([Granted, Path.Combine(Granted, "inner") + Path.DirectorySeparatorChar]);

        Assert.True(result.IsValid, string.Join(" ", result.Errors));
        Assert.All(result.CanonicalRoots, r => Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), r, StringComparison.Ordinal));
        Assert.Equal(2, result.CanonicalRoots.Count);
    }

    [Fact]
    public void Duplicate_roots_are_rejected()
    {
        var (scope, _) = Build();

        Assert.False(scope.ValidateRoots([Granted, Granted + Path.DirectorySeparatorChar]).IsValid);
    }

    [Fact]
    public void Roots_that_are_links_inside_links_outside_protected_missing_or_files_are_rejected()
    {
        var linkRoot = Path.Combine(_root, "link-root");
        Directory.CreateDirectory(linkRoot);
        var junction = Path.Combine(_root, "junction");
        Directory.CreateDirectory(Path.Combine(junction, "sub"));
        var file = Path.Combine(Granted, "file.txt");
        File.WriteAllText(file, "x");
        var secrets = Path.Combine(_root, "secrets");
        Directory.CreateDirectory(secrets);
        var outside = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);

        try
        {
            var resolution = new MappedReparseResolution()
                .Link(linkRoot, Other)
                .Link(junction, Other);
            var (scope, _) = Build(resolution);

            foreach (var root in new[]
                     {
                         linkRoot,                          // the root itself is a link
                         Path.Combine(junction, "sub"),     // inside a link
                         outside,                           // outside the global policy
                         secrets,                           // protected (HAMMOR secrets)
                         Path.Combine(_root, "missing"),    // does not exist
                         file,                              // a file, not a directory
                         "  ",                              // blank
                     })
            {
                Assert.False(scope.ValidateRoots([root]).IsValid, $"Root should be rejected: '{root}'");
            }
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void More_than_the_maximum_number_of_roots_is_rejected()
    {
        var (scope, _) = Build();
        var roots = Enumerable.Range(0, TaskGrantValidator.MaxAllowedRoots + 1)
            .Select(i => Directory.CreateDirectory(Path.Combine(Granted, $"r{i}")).FullName)
            .ToList();

        Assert.False(scope.ValidateRoots(roots).IsValid);
        Assert.NotEmpty(TaskGrantValidator.ValidateStructure(
            TestTasks.Grant("t", _clock) with { AllowedRoots = roots }, _clock.GetUtcNow()));
    }

    [Fact]
    public void Grant_approval_name_cannot_be_registered_as_a_tool()
    {
        var registry = new ToolRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.Register(new FakeTool("task.grant", ToolPermission.Read)));
        Assert.Throws<InvalidOperationException>(() => registry.Register(new FakeTool("TASK.GRANT", ToolPermission.Read)));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
