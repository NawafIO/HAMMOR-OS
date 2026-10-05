using HAMMOR.Core.Agent;
using HAMMOR.Core.Ai;
using HAMMOR.Core.Audit;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Memory;
using HAMMOR.Core.Permissions;
using HAMMOR.Core.Projects;
using HAMMOR.Core.Storage;
using HAMMOR.Core.Tasks;
using HAMMOR.Core.Tests.TestDoubles;
using HAMMOR.Core.Tools;
using HAMMOR.Core.Tools.Filesystem;
using HAMMOR.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HAMMOR.Core.Tests.Tasks;

/// <summary>
/// End-to-end: real SqliteTaskStore, TaskRunner, AgentPipeline and AgentLoop;
/// only the model provider and the tools are test doubles.
/// </summary>
public sealed class UnattendedTaskRunnerTests : IDisposable
{
    private sealed class ScriptedProvider(
        Func<int, CancellationToken, Task<ModelResponseTurn>> script) : IToolCallingProvider
    {
        public string ProviderId => "claude";

        public string DisplayName => "Claude";

        public int CallCount { get; private set; }

        public IReadOnlyList<ToolDefinition> LastToolsOffered { get; private set; } = [];

        public Task<ProviderAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ProviderAvailability.Ready("ok"));

        public Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default) =>
            throw new AiProviderException("tool path expected");

        public Task<ModelResponseTurn> CompleteWithToolsAsync(
            AiToolAwareRequest request,
            IReadOnlyList<ToolDefinition> tools,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastToolsOffered = tools;
            return script(CallCount++, cancellationToken);
        }
    }

    private sealed class RecordingAuditLog : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public Func<AuditEntry, bool>? FailWhen { get; set; }

        public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (FailWhen?.Invoke(entry) == true)
            {
                throw new IOException("audit store unavailable");
            }

            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int limit = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditEntry>>(Entries);
    }

    private sealed class CountingMemoryStore : IMemoryStore
    {
        public int SaveCount { get; private set; }

        public Task<MemoryEntry> SaveAsync(MemoryEntry entry, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(entry);
        }

        public Task<IReadOnlyList<MemoryEntry>> SearchAsync(string q, int limit = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryEntry>>([]);

        public Task<IReadOnlyList<MemoryEntry>> GetRecentAsync(string? projectId = null, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryEntry>>([]);

        public Task<MemoryEntry?> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<MemoryEntry?>(null);

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoProjectStore : IProjectStore
    {
        public Task<HammorProject> CreateAsync(HammorProject p, CancellationToken cancellationToken = default) => Task.FromResult(p);

        public Task<HammorProject?> GetAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<HammorProject?>(null);

        public Task<HammorProject> UpdateAsync(HammorProject p, CancellationToken cancellationToken = default) => Task.FromResult(p);

        public Task<IReadOnlyList<HammorProject>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HammorProject>>([]);

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Evaluator that always wants a person to confirm, to prove unattended runs cannot supply one.</summary>
    private sealed class AlwaysAskEvaluator : IPermissionEvaluator
    {
        public PermissionDecision Evaluate(ITool tool, ToolInvocation invocation) =>
            PermissionDecision.RequireConfirmation("needs a person");

        public async Task<PermissionDecision> AuthoriseAsync(
            ITool tool,
            ToolInvocation invocation,
            IConfirmationService confirmation,
            CancellationToken cancellationToken = default)
        {
            var approved = await confirmation.RequestApprovalAsync(
                new ConfirmationRequest(tool.Name, tool.Permission, "needs a person", string.Empty),
                cancellationToken);

            return approved
                ? PermissionDecision.Allow("approved")
                : PermissionDecision.Deny($"User declined '{tool.Name}'.");
        }
    }

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "hammor-tests", Guid.NewGuid().ToString("n"));

    private readonly TestClock _clock = new(TestTasks.Start);
    private readonly SqliteTaskStore _store;
    private readonly RecordingAuditLog _audit = new();
    private readonly CountingMemoryStore _memory = new();

    // The interactive confirmation service says YES to everything: an unattended run must never reach it.
    private readonly RecordingConfirmationService _interactive = new(approve: true);
    private readonly FakeTool _searchTool = new("memory.search", ToolPermission.Read);
    private readonly FakeTool _statusTool = new("git.status", ToolPermission.Read);
    private readonly FakeTool _saveTool = new("memory.save", ToolPermission.Write);

    public UnattendedTaskRunnerTests()
    {
        var database = new SqliteDatabase(new HammorPaths(_root), NullLogger<SqliteDatabase>.Instance);
        database.Migrate();
        _store = new SqliteTaskStore(database, NullLogger<SqliteTaskStore>.Instance, _clock);
    }

    private TaskRunner BuildRunner(
        ScriptedProvider provider,
        IPermissionEvaluator? evaluator = null,
        IEnumerable<ITool>? extraTools = null,
        TaskPathScope? pathScope = null)
    {
        var registry = new ToolRegistry();
        registry.Register(_searchTool);
        registry.Register(_statusTool);
        registry.Register(_saveTool);
        foreach (var tool in extraTools ?? Array.Empty<ITool>())
        {
            registry.Register(tool);
        }

        var configuration = new FakeConfigurationStore(new HammorConfiguration());
        evaluator ??= new PermissionEvaluator(configuration);

        var loop = new AgentLoop(
            new IAiProvider[] { provider },
            registry,
            evaluator,
            _interactive,
            _memory,
            new NoProjectStore(),
            _audit,
            configuration,
            NullLogger<AgentLoop>.Instance);

        var pipeline = new AgentPipeline(
            new IAiProvider[] { provider },
            registry,
            evaluator,
            _interactive,
            _memory,
            new NoProjectStore(),
            _audit,
            configuration,
            NullLogger<AgentPipeline>.Instance,
            loop);

        return new TaskRunner(_store, pipeline, registry, _audit, NullLogger<TaskRunner>.Instance, _clock, pathScope);
    }

    private static ModelResponseTurn CallTool(string name) =>
        new(null, [new ModelToolCall(Guid.NewGuid().ToString("n"), name, "{}")], ModelStopReasons.ToolUse);

    private static ModelResponseTurn Final(string text) =>
        new(text, Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn);

    private static ScriptedProvider Script(params ModelResponseTurn[] turns) =>
        new((i, _) => Task.FromResult(i < turns.Length ? turns[i] : Final("done")));

    private async Task<HammorTask> Reload(string id) => (await _store.GetAsync(id))!;

    // ------------------------------------------------------------------

    [Fact]
    public async Task Task_without_a_grant_is_never_executed()
    {
        var provider = Script(Final("should not run"));
        var runner = BuildRunner(provider);
        var task = await _store.CreateAsync(TestTasks.Task(_clock, withGrant: false));

        var report = await runner.RunNextAsync();

        Assert.Equal(TaskRunStatus.NoWork, report.Status);
        Assert.Equal(0, provider.CallCount);
        Assert.Equal(TaskState.Pending, (await Reload(task.Id)).State);
    }

    [Fact]
    public async Task Granted_read_tool_runs_completes_and_is_audited_as_unattended()
    {
        var provider = Script(CallTool("memory.search"), Final("all done"));
        var runner = BuildRunner(provider);
        var task = await _store.CreateAsync(TestTasks.Task(_clock, ["memory.search"]));

        var report = await runner.RunNextAsync();

        Assert.Equal(TaskRunStatus.Completed, report.Status);
        Assert.Equal(1, _searchTool.ExecutionCount);

        var done = await Reload(task.Id);
        Assert.Equal(TaskState.Completed, done.State);
        Assert.Equal("all done", done.Result);
        Assert.Equal(1, done.AttemptCount);

        Assert.Contains(_audit.Entries, e => e.Category == AuditCategory.TaskLifecycle && e.Message.Contains("Run started"));
        Assert.Contains(_audit.Entries, e => e.Category == AuditCategory.TaskLifecycle && e.Message.Contains("Run completed"));
        Assert.Contains(_audit.Entries, e => e.Category == AuditCategory.Authorisation && e.Message.Contains($"[unattended task {task.Id}]"));
        Assert.Contains(_audit.Entries, e => e.Category == AuditCategory.ToolExecution && e.Message.Contains($"[unattended task {task.Id}]"));
        Assert.Equal(0, _interactive.CallCount);
    }

    [Fact]
    public async Task Model_is_only_offered_the_granted_tools()
    {
        var provider = Script(Final("hi"));
        var runner = BuildRunner(provider);
        await _store.CreateAsync(TestTasks.Task(_clock, ["memory.search"]));

        await runner.RunNextAsync();

        Assert.Equal(new[] { "memory.search" }, provider.LastToolsOffered.Select(t => t.Name).ToArray());
    }

    [Fact]
    public async Task Tool_outside_the_grant_blocks_the_run_and_never_executes()
    {
        var provider = Script(CallTool("git.status"), Final("unreachable"));
        var runner = BuildRunner(provider);
        var task = await _store.CreateAsync(TestTasks.Task(_clock, ["memory.search"]));

        var report = await runner.RunNextAsync();

        Assert.Equal(TaskRunStatus.Blocked, report.Status);
        Assert.Equal(0, _statusTool.ExecutionCount);
        Assert.Equal(1, provider.CallCount); // the run stopped; the model was not asked again

        var blocked = await Reload(task.Id);
        Assert.Equal(TaskState.Blocked, blocked.State);
        Assert.Contains("not in the task's grant", blocked.BlockedReason);
        Assert.Equal(0, blocked.AttemptCount); // a block does not consume a retry
        Assert.Contains(_audit.Entries, e => e.Outcome == AuditOutcome.Denied && e.Message.Contains($"[unattended task {task.Id}]"));
    }

    [Fact]
    public async Task Write_tool_is_blocked_even_when_the_interactive_user_would_have_approved()
    {
        var provider = Script(CallTool("memory.save"));
        var runner = BuildRunner(provider);
        var task = await _store.CreateAsync(TestTasks.Task(_clock, ["memory.search"]));

        var report = await runner.RunNextAsync();

        Assert.Equal(TaskRunStatus.Blocked, report.Status);
        Assert.Equal(0, _saveTool.ExecutionCount);
        Assert.Equal(0, _interactive.CallCount);
        Assert.Equal(TaskState.Blocked, (await Reload(task.Id)).State);
    }

    [Fact]
    public async Task Confirmation_required_by_policy_becomes_a_block_never_an_approval()
    {
        var provider = Script(CallTool("memory.search"));
        var runner = BuildRunner(provider, new AlwaysAskEvaluator());
        var task = await _store.CreateAsync(TestTasks.Task(_clock, ["memory.search"]));

        var report = await runner.RunNextAsync();

        Assert.Equal(TaskRunStatus.Blocked, report.Status);
        Assert.Equal(0, _searchTool.ExecutionCount);
        Assert.Equal(0, _interactive.CallCount);
        Assert.Equal(TaskState.Blocked, (await Reload(task.Id)).State);
    }

    [Fact]
    public async Task Expired_grant_blocks_without_calling_the_model()
    {
        var provider = Script(Final("unreachable"));
        var runner = BuildRunner(provider);
        var task = await _store.CreateAsync(TestTasks.Task(_clock, ["memory.search"], ttl: TimeSpan.FromHours(1)));

        _clock.Advance(TimeSpan.FromHours(2));
        var report = await runner.RunNextAsync();

        Assert.Equal(0, provider.CallCount);
        Assert.Equal(TaskState.Blocked, (await Reload(task.Id)).State);
        Assert.Contains("expired", (await Reload(task.Id)).BlockedReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskRunStatus.NoWork, report.Status);
    }

    [Fact]
    public async Task Exceeding_the_tool_call_cap_fails_the_run()
    {
        var provider = Script(CallTool("memory.search"), CallTool("memory.search"), Final("unreachable"));
        var runner = BuildRunner(provider);
        var task = await _store.CreateAsync(TestTasks.Task(_clock, ["memory.search"], maxCalls: 1));

        var report = await runner.RunNextAsync();

        Assert.Equal(TaskRunStatus.Failed, report.Status);
        Assert.Equal(1, _searchTool.ExecutionCount);
        Assert.Equal(TaskState.Failed, (await Reload(task.Id)).State);
    }

    [Fact]
    public async Task Unattended_runs_do_not_write_conversation_memory()
    {
        var runner = BuildRunner(Script(Final("hello")));
        await _store.CreateAsync(TestTasks.Task(_clock));

        await runner.RunNextAsync();

        Assert.Equal(0, _memory.SaveCount);
    }

    [Fact]
    public async Task Result_is_bounded()
    {
        var runner = BuildRunner(Script(Final(new string('a', 50_000))));
        var task = await _store.CreateAsync(TestTasks.Task(_clock));

        await runner.RunNextAsync();

        var done = await Reload(task.Id);
        Assert.Equal(TaskState.Completed, done.State);
        Assert.True(done.Result!.Length < 5_000);
    }

    [Fact]
    public async Task A_failed_audit_write_fails_the_run_and_the_tool_does_not_run()
    {
        _audit.FailWhen = e => e.Category == AuditCategory.Authorisation;
        var runner = BuildRunner(Script(CallTool("memory.search"), Final("unreachable")));
        var task = await _store.CreateAsync(TestTasks.Task(_clock, ["memory.search"]));

        var report = await runner.RunNextAsync();

        Assert.Equal(TaskRunStatus.Failed, report.Status);
        Assert.Equal(0, _searchTool.ExecutionCount);
        Assert.Equal(TaskState.Failed, (await Reload(task.Id)).State);
    }

    [Fact]
    public async Task Failure_retries_up_to_max_attempts_with_backoff_then_completes()
    {
        var provider = new ScriptedProvider((i, _) => i == 0
            ? throw new AiProviderException("transient")
            : Task.FromResult(Final("recovered")));
        var runner = BuildRunner(provider);
        var task = await _store.CreateAsync(TestTasks.Task(_clock, maxAttempts: 2));

        var first = await runner.RunNextAsync();
        Assert.Equal(TaskRunStatus.Retrying, first.Status);

        var waiting = await Reload(task.Id);
        Assert.Equal(TaskState.Pending, waiting.State);
        Assert.True(waiting.ScheduledForUtc > _clock.GetUtcNow());

        // Not due yet: nothing runs.
        Assert.Equal(TaskRunStatus.NoWork, (await runner.RunNextAsync()).Status);

        _clock.Advance(TimeSpan.FromMinutes(5));
        var second = await runner.RunNextAsync();

        Assert.Equal(TaskRunStatus.Completed, second.Status);
        Assert.Equal(2, (await Reload(task.Id)).AttemptCount);
    }

    [Fact]
    public async Task Runner_never_retries_beyond_the_attempt_limit_even_if_a_row_says_more()
    {
        var provider = new ScriptedProvider((_, _) => throw new AiProviderException("down"));
        var runner = BuildRunner(provider);
        var created = await _store.CreateAsync(TestTasks.Task(_clock, ttl: TimeSpan.FromDays(2)));

        // A stored row raised past the limit after creation (UpdateAsync does not police it).
        await _store.UpdateAsync(created with { MaxAttempts = 50 });

        var reports = new List<TaskRunStatus>();
        for (var i = 0; i < 20; i++)
        {
            var report = await runner.RunNextAsync();
            if (report.Status != TaskRunStatus.NoWork)
            {
                reports.Add(report.Status);
            }

            if (report.Status == TaskRunStatus.Failed)
            {
                break;
            }

            _clock.Advance(TimeSpan.FromMinutes(16)); // longer than the 15-minute backoff cap
        }

        Assert.Equal(TaskRunStatus.Failed, reports[^1]);
        Assert.Equal(HammorTask.MaxAttemptsLimit - 1, reports.Count(s => s == TaskRunStatus.Retrying));
        Assert.Equal(HammorTask.MaxAttemptsLimit, provider.CallCount);
        Assert.Equal(TaskState.Failed, (await Reload(created.Id)).State);
    }

    [Fact]
    public async Task Failure_on_the_last_attempt_is_terminal()
    {
        var provider = new ScriptedProvider((_, _) => throw new AiProviderException("down"));
        var runner = BuildRunner(provider);
        var task = await _store.CreateAsync(TestTasks.Task(_clock, maxAttempts: 1));

        var report = await runner.RunNextAsync();

        Assert.Equal(TaskRunStatus.Failed, report.Status);
        Assert.Equal(TaskState.Failed, (await Reload(task.Id)).State);
    }

    [Fact]
    public async Task Only_one_task_runs_at_a_time()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ScriptedProvider(async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Final("unreachable");
        });
        var runner = BuildRunner(provider);
        var first = await _store.CreateAsync(TestTasks.Task(_clock));
        var second = await _store.CreateAsync(TestTasks.Task(_clock));

        var running = runner.RunNextAsync();
        await started.Task;

        var concurrent = await runner.RunNextAsync();
        Assert.Equal(TaskRunStatus.Busy, concurrent.Status);
        Assert.Equal(1, provider.CallCount);

        Assert.True(runner.RequestCancel((await _store.ListAsync([TaskState.Running]))[0].Id));
        var report = await running;

        Assert.Equal(TaskRunStatus.Cancelled, report.Status);
        Assert.Equal(1, provider.CallCount); // the second task never started
        var states = new[] { (await Reload(first.Id)).State, (await Reload(second.Id)).State };
        Assert.Contains(TaskState.Cancelled, states);
        Assert.Contains(TaskState.Pending, states);
    }

    [Fact]
    public async Task Cancellation_mid_run_is_recorded_and_audited()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ScriptedProvider(async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Final("unreachable");
        });
        var runner = BuildRunner(provider);
        var task = await _store.CreateAsync(TestTasks.Task(_clock));

        var running = runner.RunNextAsync();
        await started.Task;
        runner.RequestCancel(task.Id);
        var report = await running;

        Assert.Equal(TaskRunStatus.Cancelled, report.Status);
        Assert.Equal(TaskState.Cancelled, (await Reload(task.Id)).State);
        Assert.Contains(_audit.Entries, e => e.Category == AuditCategory.TaskLifecycle && e.Message.Contains("cancelled"));
    }

    [Fact]
    public async Task Shutdown_token_cancels_the_run()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ScriptedProvider(async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Final("unreachable");
        });
        var runner = BuildRunner(provider);
        var task = await _store.CreateAsync(TestTasks.Task(_clock));

        using var shutdown = new CancellationTokenSource();
        var running = runner.RunNextAsync(shutdown.Token);
        await started.Task;
        shutdown.Cancel();
        var report = await running;

        Assert.Equal(TaskRunStatus.Cancelled, report.Status);
        Assert.Equal(TaskState.Cancelled, (await Reload(task.Id)).State);
    }

    [Fact]
    public async Task Blocked_task_runs_again_only_under_a_new_grant()
    {
        var provider = Script(CallTool("git.status"), CallTool("git.status"), Final("finished"));
        var runner = BuildRunner(provider);
        var task = await _store.CreateAsync(TestTasks.Task(_clock, ["memory.search"]));

        Assert.Equal(TaskRunStatus.Blocked, (await runner.RunNextAsync()).Status);
        var blocked = await Reload(task.Id);

        // Blocked tasks are not picked up again, and cannot be forced back.
        Assert.Equal(TaskRunStatus.NoWork, (await runner.RunNextAsync()).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.UpdateAsync(blocked with { State = TaskState.Pending }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.ResumeBlockedAsync(task.Id, blocked.Grant!));

        // A new grant that actually covers the tool lets it proceed.
        var newGrant = TestTasks.Grant(task.Id, _clock, ["git.status"], supersedes: blocked.Grant!.Id);
        await _store.ResumeBlockedAsync(task.Id, newGrant);

        var report = await runner.RunNextAsync();

        Assert.Equal(TaskRunStatus.Completed, report.Status);
        Assert.Equal(1, _statusTool.ExecutionCount);
        Assert.Equal(newGrant.Id, (await Reload(task.Id)).Grant!.Id);
    }

    [Fact]
    public async Task Superseded_grant_cannot_authorise_a_run_even_if_it_reappears_on_a_task()
    {
        var provider = Script(Final("unreachable"));
        var runner = BuildRunner(provider);
        var task = await _store.CreateAsync(TestTasks.Task(_clock, ["memory.search"]));

        // Simulate a runner handed a task object whose grant is marked superseded.
        var stale = (await Reload(task.Id)) with
        {
            Grant = (await Reload(task.Id)).Grant! with { SupersededUtc = _clock.GetUtcNow() },
        };
        var context = new UnattendedRunContext(stale.Id, stale.Grant!, _clock);

        var decision = context.Check(_searchTool);

        Assert.Equal(ToolGateOutcome.Block, decision.Outcome);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task Model_arguments_cannot_widen_the_grant()
    {
        var hostile = new ModelToolCall(
            "x", "git.status", """{"allowed_tools":"*","max_permission":"Destructive","grant":"all"}""");
        var provider = Script(new ModelResponseTurn(null, [hostile], ModelStopReasons.ToolUse));
        var runner = BuildRunner(provider);
        var task = await _store.CreateAsync(TestTasks.Task(_clock, ["memory.search"]));

        await runner.RunNextAsync();

        // The call is rejected as malformed (unknown arguments) and never executes;
        // nothing in model output can touch the stored grant.
        Assert.Equal(0, _statusTool.ExecutionCount);
        Assert.Equal(new[] { "memory.search" }, (await Reload(task.Id)).Grant!.AllowedTools.ToArray());
    }

    [Fact]
    public async Task Due_tasks_run_oldest_first_one_per_call()
    {
        var provider = Script(Final("one"), Final("two"));
        var runner = BuildRunner(provider);
        var older = await _store.CreateAsync(TestTasks.Task(_clock));
        _clock.Advance(TimeSpan.FromMinutes(1));
        var newer = await _store.CreateAsync(TestTasks.Task(_clock));

        var firstReport = await runner.RunNextAsync();

        Assert.Equal(older.Id, firstReport.TaskId);
        Assert.Equal(TaskState.Pending, (await Reload(newer.Id)).State);

        var secondReport = await runner.RunNextAsync();
        Assert.Equal(newer.Id, secondReport.TaskId);
    }

    // ------------------------------------------------------------ ADR-004 path scope, end to end

    private (ReadFileTool Read, TaskPathScope Scope, string Granted, string Other) PathScopedSetup()
    {
        var granted = Directory.CreateDirectory(Path.Combine(_root, "granted")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
        File.WriteAllText(Path.Combine(granted, "note.txt"), "inside");
        File.WriteAllText(Path.Combine(other, "private.txt"), "outside");

        var resolution = new ManagedPathResolution();
        var policy = FilesystemPolicyFactory.CreateForRoot(_root, resolution);
        return (new ReadFileTool(policy), new TaskPathScope(policy, resolution), granted, other);
    }

    private static ModelResponseTurn Read(string path) =>
        new(null, [new ModelToolCall(Guid.NewGuid().ToString("n"), "filesystem.read_file", System.Text.Json.JsonSerializer.Serialize(new { path }))], ModelStopReasons.ToolUse);

    private async Task<HammorTask> CreateScopedTaskAsync(TaskPathScope scope, string granted)
    {
        var roots = scope.ValidateRoots([granted]);
        Assert.True(roots.IsValid, string.Join(" ", roots.Errors));
        var task = TestTasks.Task(_clock, ["filesystem.read_file"]);
        return await _store.CreateAsync(task with { Grant = task.Grant! with { AllowedRoots = roots.CanonicalRoots } });
    }

    [Fact]
    public async Task Real_read_inside_the_granted_root_completes()
    {
        var (read, scope, granted, _) = PathScopedSetup();
        var runner = BuildRunner(Script(Read(Path.Combine(granted, "note.txt")), Final("read it")), extraTools: [read], pathScope: scope);
        var task = await CreateScopedTaskAsync(scope, granted);

        var report = await runner.RunNextAsync();

        Assert.Equal(TaskRunStatus.Completed, report.Status);
        Assert.Contains(_audit.Entries, e => e.Category == AuditCategory.ToolExecution
            && e.Outcome == AuditOutcome.Succeeded && e.Message.Contains($"[unattended task {task.Id}]"));
    }

    [Fact]
    public async Task Real_read_outside_the_granted_root_blocks_before_the_tool_runs()
    {
        var (read, scope, granted, other) = PathScopedSetup();
        var runner = BuildRunner(Script(Read(Path.Combine(other, "private.txt")), Final("unreachable")), extraTools: [read], pathScope: scope);
        var task = await CreateScopedTaskAsync(scope, granted);

        var report = await runner.RunNextAsync();

        Assert.Equal(TaskRunStatus.Blocked, report.Status);
        Assert.Contains("outside the task's granted roots", (await Reload(task.Id)).BlockedReason);
        Assert.DoesNotContain(_audit.Entries, e => e.Category == AuditCategory.ToolExecution && e.Outcome == AuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Runner_without_a_path_scope_blocks_scoped_grants_before_calling_the_model()
    {
        var (read, scope, granted, _) = PathScopedSetup();
        var provider = Script(Read(Path.Combine(granted, "note.txt")));
        var runner = BuildRunner(provider, extraTools: [read], pathScope: null);
        var task = await CreateScopedTaskAsync(scope, granted);

        await runner.RunNextAsync();

        Assert.Equal(0, provider.CallCount);
        Assert.Equal(TaskState.Blocked, (await Reload(task.Id)).State);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A lingering handle on a CI agent should not fail an otherwise-passing run.
            }
        }
    }
}
