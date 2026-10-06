using HAMMOR.Core.Audit;
using HAMMOR.Core.Permissions;
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

/// <summary>ADR-004 §3: the only way grants are created, resumed and cancelled.</summary>
public sealed class TaskAuthoringServiceTests : IDisposable
{
    private sealed class AuditLog : IAuditLog
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

    private sealed class FakeRunner : ITaskRunner
    {
        public string? RunningTaskId { get; set; }

        public List<string> CancelRequests { get; } = [];

        public Task<TaskRunReport> RunNextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TaskRunReport(null, TaskRunStatus.NoWork, "idle"));

        public bool RequestCancel(string taskId)
        {
            CancelRequests.Add(taskId);
            return string.Equals(taskId, RunningTaskId, StringComparison.Ordinal);
        }
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "hammor-tests", Guid.NewGuid().ToString("n"));
    private readonly string _files;
    private readonly TestClock _clock = new(TestTasks.Start);
    private readonly SqliteTaskStore _store;
    private readonly AuditLog _audit = new();
    private readonly FakeRunner _runner = new();
    private readonly ToolRegistry _registry = new();
    private readonly TaskPathScope _scope;

    public TaskAuthoringServiceTests()
    {
        var database = new SqliteDatabase(new HammorPaths(_root), NullLogger<SqliteDatabase>.Instance);
        database.Migrate();
        _store = new SqliteTaskStore(database, NullLogger<SqliteTaskStore>.Instance, _clock);

        _files = Path.Combine(_root, "files");
        Directory.CreateDirectory(_files);

        var resolution = new ManagedPathResolution();
        var policy = FilesystemPolicyFactory.CreateForRoot(_root, resolution);
        _scope = new TaskPathScope(policy, resolution);

        _registry.Register(new ReadFileTool(policy));
        _registry.Register(new FakeTool("memory.search", ToolPermission.Read));
        _registry.Register(new FakeTool("memory.save", ToolPermission.Write));
    }

    private TaskAuthoringService Service(IConfirmationService confirmation) =>
        new(_store, _runner, _registry, _scope, confirmation, _audit,
            NullLogger<TaskAuthoringService>.Instance, localization: null, timeProvider: _clock);

    private GrantDraft NewGrant(string[]? tools = null, string[]? roots = null, TimeSpan? ttl = null) => new()
    {
        AllowedTools = tools ?? new[] { "filesystem.read_file" },
        AllowedRoots = roots ?? new[] { _files },
        ExpiresUtc = _clock.GetUtcNow() + (ttl ?? TimeSpan.FromDays(1)),
        MaxToolCalls = 5,
    };

    private TaskDraft Draft(GrantDraft? grant = null, string prompt = "summarise the notes") => new()
    {
        Title = "Summarise notes",
        Prompt = prompt,
        Grant = grant ?? NewGrant(),
    };

    private async Task<HammorTask> CreateApprovedAsync()
    {
        var result = await Service(new RecordingConfirmationService(approve: true)).CreateAsync(Draft());
        Assert.Equal(TaskAuthoringStatus.Created, result.Status);
        return result.Task!;
    }

    private async Task<HammorTask> BlockAsync(HammorTask task)
    {
        var running = await _store.UpdateAsync(task with { State = TaskState.Running });
        return await _store.UpdateAsync(running with { State = TaskState.Blocked, BlockedReason = "needed more" });
    }

    private int IndexOf(Func<AuditEntry, bool> match) => _audit.Entries.FindIndex(e => match(e));

    // ------------------------------------------------------------ create

    [Fact]
    public async Task Approved_draft_creates_the_task_and_audits_approval_before_creation()
    {
        var confirmation = new RecordingConfirmationService(approve: true);

        var result = await Service(confirmation).CreateAsync(Draft());

        Assert.Equal(TaskAuthoringStatus.Created, result.Status);
        var stored = await _store.GetAsync(result.Task!.Id);
        Assert.NotNull(stored!.Grant);
        var grantId = stored.Grant!.Id;

        // Roots are stored in canonical form.
        Assert.Single(stored.Grant.AllowedRoots);
        Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), stored.Grant.AllowedRoots[0], StringComparison.Ordinal);
        Assert.Equal(TestTasks.Start, stored.Grant.GrantedUtc);
        Assert.Equal(ToolPermission.Read, stored.Grant.MaxPermission);

        var approved = IndexOf(e => e.Outcome == AuditOutcome.Allowed && e.Message.Contains(grantId));
        var created = IndexOf(e => e.Outcome == AuditOutcome.Succeeded && e.Message.Contains(grantId));
        Assert.True(approved >= 0 && created > approved, "approval must be audited before creation");
        Assert.All(_audit.Entries, e => Assert.Equal(AuditCategory.TaskLifecycle, e.Category));
    }

    [Fact]
    public async Task Approval_dialog_shows_the_reserved_name_and_the_exact_grant()
    {
        var confirmation = new RecordingConfirmationService(approve: true);

        var result = await Service(confirmation).CreateAsync(Draft());

        var request = confirmation.LastRequest!;
        Assert.Equal(ToolRegistry.ReservedGrantApprovalName, request.ToolName);
        Assert.Equal(ToolPermission.Read, request.Permission);
        Assert.False(string.IsNullOrWhiteSpace(request.Summary));
        Assert.Equal(
            TaskAuthoringService.DescribeGrant(result.Task!.Grant!, result.Task.Title, result.Task.ScheduledForUtc, null),
            request.Details);
    }

    [Fact]
    public void Grant_description_lists_every_field()
    {
        var grant = new TaskGrant
        {
            Id = "g2",
            TaskId = "t",
            AllowedTools = ["filesystem.read_file", "memory.search"],
            AllowedRoots = [@"C:\Work\Repo\"],
            GrantedUtc = TestTasks.Start,
            ExpiresUtc = TestTasks.Start.AddDays(2),
            MaxToolCalls = 7,
            ProjectId = "p1",
            SupersedesGrantId = "g1",
        };

        var text = TaskAuthoringService.DescribeGrant(grant, "Nightly check", TestTasks.Start.AddHours(3), "needed git");

        foreach (var expected in new[]
                 {
                     "task: Nightly check", "grant: g2", "supersedes: g1", "permission: Read",
                     "tools: filesystem.read_file, memory.search", @"roots: C:\Work\Repo\",
                     "expires: 2026-10-07 12:00:00Z", "max tool calls: 7", "schedule: 2026-10-05 15:00:00Z",
                     "project: p1", "blocked because: needed git",
                 })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Refusal_or_dismissal_creates_nothing_and_is_audited_as_refused()
    {
        var result = await Service(new RecordingConfirmationService(approve: false)).CreateAsync(Draft());

        Assert.Equal(TaskAuthoringStatus.Refused, result.Status);
        Assert.Empty(await _store.ListAsync());
        Assert.Contains(_audit.Entries, e => e.Outcome == AuditOutcome.Denied && e.Message.Contains("refused"));
        Assert.DoesNotContain(_audit.Entries, e => e.Outcome == AuditOutcome.Allowed);
    }

    [Fact]
    public async Task Invalid_drafts_never_reach_the_dialog()
    {
        var confirmation = new RecordingConfirmationService(approve: true);
        var service = Service(confirmation);

        var drafts = new[]
        {
            Draft(NewGrant(tools: ["memory.save"], roots: [])),                    // Write tool
            Draft(NewGrant(roots: [])),                                            // path tool without roots
            Draft(NewGrant(roots: [Path.Combine(_root, "missing")])),              // root does not exist
            Draft(NewGrant(ttl: TimeSpan.FromMinutes(-1))),                        // expired
            Draft(NewGrant(ttl: TaskGrantValidator.MaxLifetime + TimeSpan.FromDays(1))), // too long
            Draft(prompt: "  "),                                                      // no prompt
            Draft() with { MaxAttempts = 0 },
            Draft() with { ScheduledForUtc = _clock.GetUtcNow().AddDays(2) },         // scheduled after expiry
        };

        foreach (var draft in drafts)
        {
            var result = await service.CreateAsync(draft);
            Assert.Equal(TaskAuthoringStatus.Invalid, result.Status);
            Assert.NotEmpty(result.Errors);
        }

        Assert.Equal(0, confirmation.CallCount);
        Assert.Empty(await _store.ListAsync());
    }

    [Fact]
    public async Task Failed_approval_audit_creates_nothing()
    {
        _audit.FailWhen = e => e.Outcome == AuditOutcome.Allowed;

        var result = await Service(new RecordingConfirmationService(approve: true)).CreateAsync(Draft());

        Assert.Equal(TaskAuthoringStatus.Failed, result.Status);
        Assert.Empty(await _store.ListAsync());
    }

    [Fact]
    public async Task Text_only_task_needs_no_roots()
    {
        var result = await Service(new RecordingConfirmationService(approve: true))
            .CreateAsync(Draft(NewGrant(tools: [], roots: [])));

        Assert.Equal(TaskAuthoringStatus.Created, result.Status);
        Assert.Empty(result.Task!.Grant!.AllowedTools);
    }

    // ------------------------------------------------------------ resume

    [Fact]
    public async Task Resume_creates_a_new_grant_that_supersedes_the_old_one_and_audits_both_ids()
    {
        var blocked = await BlockAsync(await CreateApprovedAsync());
        var oldId = blocked.Grant!.Id;
        var confirmation = new RecordingConfirmationService(approve: true);

        var result = await Service(confirmation).ResumeAsync(blocked.Id, NewGrant(tools: ["filesystem.read_file", "memory.search"]));

        Assert.Equal(TaskAuthoringStatus.Resumed, result.Status);
        var resumed = await _store.GetAsync(blocked.Id);
        Assert.Equal(TaskState.Pending, resumed!.State);
        Assert.NotEqual(oldId, resumed.Grant!.Id);
        Assert.Equal(oldId, resumed.Grant.SupersedesGrantId);
        Assert.Contains("supersedes: " + oldId, confirmation.LastRequest!.Details, StringComparison.Ordinal);
        Assert.Contains("blocked because: needed more", confirmation.LastRequest.Details, StringComparison.Ordinal);

        Assert.Contains(_audit.Entries, e => e.Outcome == AuditOutcome.Allowed
            && e.Message.Contains(resumed.Grant.Id) && e.Message.Contains(oldId));
        Assert.Contains(_audit.Entries, e => e.Outcome == AuditOutcome.Succeeded
            && e.Message.Contains("superseded") && e.Message.Contains(oldId) && e.Message.Contains(resumed.Grant.Id));
    }

    [Fact]
    public async Task Resume_refused_leaves_the_task_blocked_with_its_old_grant()
    {
        var blocked = await BlockAsync(await CreateApprovedAsync());

        var result = await Service(new RecordingConfirmationService(approve: false)).ResumeAsync(blocked.Id, NewGrant());

        Assert.Equal(TaskAuthoringStatus.Refused, result.Status);
        var current = await _store.GetAsync(blocked.Id);
        Assert.Equal(TaskState.Blocked, current!.State);
        Assert.Equal(blocked.Grant!.Id, current.Grant!.Id);
    }

    [Fact]
    public async Task Resume_of_a_task_that_is_not_blocked_is_rejected_without_a_dialog()
    {
        var pending = await CreateApprovedAsync();
        var confirmation = new RecordingConfirmationService(approve: true);

        var result = await Service(confirmation).ResumeAsync(pending.Id, NewGrant());

        Assert.Equal(TaskAuthoringStatus.NotAllowed, result.Status);
        Assert.Equal(0, confirmation.CallCount);
        Assert.Equal(TaskAuthoringStatus.NotFound, (await Service(confirmation).ResumeAsync("missing", NewGrant())).Status);
    }

    [Fact]
    public async Task Resume_with_an_invalid_draft_never_reaches_the_dialog()
    {
        var blocked = await BlockAsync(await CreateApprovedAsync());
        var confirmation = new RecordingConfirmationService(approve: true);

        var result = await Service(confirmation).ResumeAsync(blocked.Id, NewGrant(tools: ["memory.save"], roots: []));

        Assert.Equal(TaskAuthoringStatus.Invalid, result.Status);
        Assert.Equal(0, confirmation.CallCount);
        Assert.Equal(TaskState.Blocked, (await _store.GetAsync(blocked.Id))!.State);
    }

    [Fact]
    public async Task Failed_resume_approval_audit_changes_nothing()
    {
        var blocked = await BlockAsync(await CreateApprovedAsync());
        _audit.FailWhen = e => e.Outcome == AuditOutcome.Allowed;

        var result = await Service(new RecordingConfirmationService(approve: true)).ResumeAsync(blocked.Id, NewGrant());

        Assert.Equal(TaskAuthoringStatus.Failed, result.Status);
        var current = await _store.GetAsync(blocked.Id);
        Assert.Equal(TaskState.Blocked, current!.State);
        Assert.Equal(blocked.Grant!.Id, current.Grant!.Id);
    }

    // ------------------------------------------------------------ cancel

    [Fact]
    public async Task Cancel_pending_and_blocked_tasks_through_the_store_and_audit_it()
    {
        var service = Service(new RecordingConfirmationService(approve: true));
        var pending = await CreateApprovedAsync();
        var blocked = await BlockAsync(await CreateApprovedAsync());

        Assert.Equal(TaskAuthoringStatus.Cancelled, (await service.CancelAsync(pending.Id)).Status);
        Assert.Equal(TaskAuthoringStatus.Cancelled, (await service.CancelAsync(blocked.Id)).Status);

        Assert.Equal(TaskState.Cancelled, (await _store.GetAsync(pending.Id))!.State);
        Assert.Equal(TaskState.Cancelled, (await _store.GetAsync(blocked.Id))!.State);
        Assert.Empty(_runner.CancelRequests);
        Assert.Contains(_audit.Entries, e => e.Subject == $"task:{pending.Id}" && e.Message.Contains("Cancel requested"));
        Assert.Contains(_audit.Entries, e => e.Subject == $"task:{blocked.Id}" && e.Outcome == AuditOutcome.Succeeded && e.Message.Contains("cancelled"));
    }

    [Fact]
    public async Task Cancel_running_task_asks_the_runner()
    {
        var task = await CreateApprovedAsync();
        await _store.UpdateAsync(task with { State = TaskState.Running });
        _runner.RunningTaskId = task.Id;

        var result = await Service(new RecordingConfirmationService(approve: true)).CancelAsync(task.Id);

        Assert.Equal(TaskAuthoringStatus.CancelRequested, result.Status);
        Assert.Equal(new[] { task.Id }, _runner.CancelRequests);
        Assert.Equal(TaskState.Running, (await _store.GetAsync(task.Id))!.State); // the runner records the outcome
        Assert.Contains(_audit.Entries, e => e.Subject == $"task:{task.Id}" && e.Message.Contains("running task was requested"));
    }

    [Fact]
    public async Task Cancel_of_finished_or_missing_task_is_rejected()
    {
        var service = Service(new RecordingConfirmationService(approve: true));
        var task = await CreateApprovedAsync();
        await _store.UpdateAsync(task with { State = TaskState.Completed });

        Assert.Equal(TaskAuthoringStatus.NotAllowed, (await service.CancelAsync(task.Id)).Status);
        Assert.Equal(TaskState.Completed, (await _store.GetAsync(task.Id))!.State);
        Assert.Equal(TaskAuthoringStatus.NotFound, (await service.CancelAsync("missing")).Status);
    }

    [Fact]
    public async Task Store_cancel_never_overwrites_a_running_task()
    {
        var task = await CreateApprovedAsync();
        await _store.UpdateAsync(task with { State = TaskState.Running });

        Assert.Null(await _store.TryCancelAsync(task.Id));
        Assert.Equal(TaskState.Running, (await _store.GetAsync(task.Id))!.State);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

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
