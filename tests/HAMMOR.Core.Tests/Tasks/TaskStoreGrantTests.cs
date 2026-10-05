using HAMMOR.Core.Storage;
using HAMMOR.Core.Tasks;
using HAMMOR.Core.Tools;
using HAMMOR.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HAMMOR.Core.Tests.Tasks;

/// <summary>Grant immutability, supersession and legal transitions against a real SQLite file.</summary>
public sealed class TaskStoreGrantTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "hammor-tests", Guid.NewGuid().ToString("n"));

    private readonly TestClock _clock = new(TestTasks.Start);
    private readonly HammorPaths _paths;
    private readonly SqliteDatabase _database;
    private readonly SqliteTaskStore _store;

    public TaskStoreGrantTests()
    {
        _paths = new HammorPaths(_root);
        _database = new SqliteDatabase(_paths, NullLogger<SqliteDatabase>.Instance);
        _database.Migrate();
        _store = new SqliteTaskStore(_database, NullLogger<SqliteTaskStore>.Instance, _clock);
    }

    private async Task<HammorTask> BlockedTaskAsync(string[]? tools = null)
    {
        var created = await _store.CreateAsync(TestTasks.Task(_clock, tools));
        var running = await _store.UpdateAsync(created with { State = TaskState.Running });
        return await _store.UpdateAsync(
            running with { State = TaskState.Blocked, BlockedReason = "needed more" });
    }

    [Fact]
    public async Task Grant_round_trips_with_the_task()
    {
        var created = await _store.CreateAsync(TestTasks.Task(_clock, ["memory.search", "git.status"], maxCalls: 7));

        var loaded = await _store.GetAsync(created.Id);

        Assert.NotNull(loaded!.Grant);
        Assert.Equal(created.Grant!.Id, loaded.Grant!.Id);
        Assert.Equal(new[] { "memory.search", "git.status" }, loaded.Grant.AllowedTools);
        Assert.Equal(7, loaded.Grant.MaxToolCalls);
        Assert.Equal(ToolPermission.Read, loaded.Grant.MaxPermission);
        Assert.Equal("do the thing", loaded.Prompt);
        Assert.Null(loaded.Grant.SupersededUtc);
    }

    [Fact]
    public async Task Task_without_a_grant_stays_interactive_only()
    {
        var created = await _store.CreateAsync(new HammorTask { Title = "plain" });

        Assert.Null((await _store.GetAsync(created.Id))!.Grant);
    }

    [Fact]
    public async Task Creating_a_task_with_an_out_of_policy_grant_is_rejected()
    {
        var task = TestTasks.Task(_clock);

        await Assert.ThrowsAsync<ArgumentException>(() => _store.CreateAsync(
            task with { Grant = TestTasks.Grant(task.Id, _clock, permission: ToolPermission.Write) }));

        await Assert.ThrowsAsync<ArgumentException>(() => _store.CreateAsync(
            task with { Grant = TestTasks.Grant(task.Id, _clock) with { ExpiresUtc = default } }));

        await Assert.ThrowsAsync<ArgumentException>(() => _store.CreateAsync(
            task with { Grant = TestTasks.Grant("someone-else", _clock) }));

        Assert.Null(await _store.GetAsync(task.Id));
    }

    [Fact]
    public async Task Update_cannot_change_or_remove_the_grant()
    {
        var created = await _store.CreateAsync(TestTasks.Task(_clock));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.UpdateAsync(created with { Grant = null }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.UpdateAsync(created with { Grant = TestTasks.Grant(created.Id, _clock) }));

        // Same grant id with a later expiry is still a different grant object: extending is not an edit path.
        var loaded = await _store.GetAsync(created.Id);
        Assert.Equal(created.Grant!.ExpiresUtc, loaded!.Grant!.ExpiresUtc);
    }

    [Fact]
    public async Task Blocked_cannot_return_to_pending_or_running_through_update()
    {
        var blocked = await BlockedTaskAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.UpdateAsync(blocked with { State = TaskState.Pending }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.UpdateAsync(blocked with { State = TaskState.Running }));

        Assert.Equal(TaskState.Blocked, (await _store.GetAsync(blocked.Id))!.State);
    }

    [Fact]
    public async Task Terminal_tasks_cannot_be_reopened()
    {
        var created = await _store.CreateAsync(new HammorTask { Title = "done" });
        var done = await _store.UpdateAsync(created with { State = TaskState.Completed });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.UpdateAsync(done with { State = TaskState.Pending }));
    }

    [Fact]
    public async Task Resume_with_a_new_grant_supersedes_the_old_one()
    {
        var blocked = await BlockedTaskAsync(["memory.search"]);
        var oldGrant = blocked.Grant!;
        var newGrant = TestTasks.Grant(
            blocked.Id, _clock, ["memory.search"], supersedes: oldGrant.Id);

        var resumed = await _store.ResumeBlockedAsync(blocked.Id, newGrant);

        Assert.Equal(TaskState.Pending, resumed.State);
        Assert.Null(resumed.BlockedReason);
        Assert.Equal(newGrant.Id, resumed.Grant!.Id);
        Assert.NotEqual(oldGrant.Id, resumed.Grant.Id);
        Assert.Null(resumed.Grant.SupersededUtc);
    }

    [Fact]
    public async Task Resume_rejects_reusing_or_extending_the_blocked_grant()
    {
        var blocked = await BlockedTaskAsync(["memory.search"]);
        var old = blocked.Grant!;

        // Same grant.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.ResumeBlockedAsync(blocked.Id, old));

        // Same id, later expiry: "extension" is just reuse under another name.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.ResumeBlockedAsync(blocked.Id, old with { ExpiresUtc = old.ExpiresUtc.AddHours(1), SupersedesGrantId = old.Id }));

        // New id but not declaring what it supersedes.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.ResumeBlockedAsync(blocked.Id, TestTasks.Grant(blocked.Id, _clock)));

        Assert.Equal(TaskState.Blocked, (await _store.GetAsync(blocked.Id))!.State);
        Assert.Equal(old.Id, (await _store.GetAsync(blocked.Id))!.Grant!.Id);
    }

    [Fact]
    public async Task Resume_rejects_invalid_new_grants_and_non_blocked_tasks()
    {
        var blocked = await BlockedTaskAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _store.ResumeBlockedAsync(
            blocked.Id,
            TestTasks.Grant(blocked.Id, _clock, permission: ToolPermission.Write, supersedes: blocked.Grant!.Id)));

        await Assert.ThrowsAsync<ArgumentException>(() => _store.ResumeBlockedAsync(
            blocked.Id,
            TestTasks.Grant(blocked.Id, _clock, supersedes: blocked.Grant!.Id) with { ExpiresUtc = _clock.GetUtcNow().AddMinutes(-1) }));

        var pending = await _store.CreateAsync(TestTasks.Task(_clock));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ResumeBlockedAsync(
            pending.Id, TestTasks.Grant(pending.Id, _clock, supersedes: pending.Grant!.Id)));
    }

    [Fact]
    public async Task A_superseded_grant_is_retired_permanently()
    {
        var blocked = await BlockedTaskAsync();
        var oldId = blocked.Grant!.Id;
        await _store.ResumeBlockedAsync(
            blocked.Id, TestTasks.Grant(blocked.Id, _clock, supersedes: oldId));

        // Second block and resume: the first grant must stay retired and not become current again.
        var current = await _store.GetAsync(blocked.Id);
        var running = await _store.UpdateAsync(current! with { State = TaskState.Running });
        var blockedAgain = await _store.UpdateAsync(running with { State = TaskState.Blocked });
        var third = TestTasks.Grant(blocked.Id, _clock, supersedes: blockedAgain.Grant!.Id);
        await _store.ResumeBlockedAsync(blocked.Id, third);

        var final = await _store.GetAsync(blocked.Id);
        Assert.Equal(third.Id, final!.Grant!.Id);

        // The retired grants can never be re-linked: a re-resume pointing at the first grant is refused.
        var runningThird = await _store.UpdateAsync(final with { State = TaskState.Running });
        var again = await _store.UpdateAsync(runningThird with { State = TaskState.Blocked });
        await Assert.ThrowsAnyAsync<Exception>(() => _store.ResumeBlockedAsync(
            blocked.Id, TestTasks.Grant(blocked.Id, _clock, supersedes: again.Grant!.Id) with { Id = oldId }));
    }

    [Fact]
    public async Task Database_refuses_to_edit_or_delete_stored_grants()
    {
        var created = await _store.CreateAsync(TestTasks.Task(_clock));

        using var connection = _database.OpenConnection();

        using var edit = connection.CreateCommand();
        edit.CommandText = "UPDATE task_grants SET expires_utc = '9999-12-31T00:00:00.0000000+00:00' WHERE id = $id;";
        edit.Parameters.AddWithValue("$id", created.Grant!.Id);
        Assert.Throws<SqliteException>(() => edit.ExecuteNonQuery());

        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM task_grants WHERE id = $id;";
        delete.Parameters.AddWithValue("$id", created.Grant!.Id);
        Assert.Throws<SqliteException>(() => delete.ExecuteNonQuery());

        using var swap = connection.CreateCommand();
        swap.CommandText = "UPDATE tasks SET grant_id = 'other' WHERE id = $id;";
        swap.Parameters.AddWithValue("$id", created.Id);
        Assert.Throws<SqliteException>(() => swap.ExecuteNonQuery());
    }

    [Fact]
    public void Migration_upgrades_a_phase_4_database_and_keeps_existing_tasks_interactive_only()
    {
        var root = Path.Combine(Path.GetTempPath(), "hammor-tests", Guid.NewGuid().ToString("n"));
        try
        {
            var paths = new HammorPaths(root);
            paths.EnsureCreated();

            // A database exactly as Phase 4 created it: no grant columns, no grants table.
            using (var legacy = new SqliteConnection($"Data Source={paths.DatabaseFile}"))
            {
                legacy.Open();
                using var create = legacy.CreateCommand();
                create.CommandText = """
                    CREATE TABLE tasks (
                        id TEXT PRIMARY KEY, title TEXT NOT NULL, description TEXT NULL,
                        state INTEGER NOT NULL, project_id TEXT NULL, created_utc TEXT NOT NULL,
                        started_utc TEXT NULL, completed_utc TEXT NULL, scheduled_for_utc TEXT NULL,
                        attempt_count INTEGER NOT NULL DEFAULT 0, max_attempts INTEGER NOT NULL DEFAULT 1,
                        error TEXT NULL, result TEXT NULL);
                    INSERT INTO tasks (id, title, state, created_utc)
                        VALUES ('old', 'Legacy', 0, '2026-01-01T00:00:00.0000000+00:00');
                    """;
                create.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();

            var database = new SqliteDatabase(paths, NullLogger<SqliteDatabase>.Instance);
            database.Migrate();
            database.Migrate();

            var store = new SqliteTaskStore(database, NullLogger<SqliteTaskStore>.Instance, _clock);
            var loaded = store.GetAsync("old").GetAwaiter().GetResult();

            Assert.NotNull(loaded);
            Assert.Equal("Legacy", loaded!.Title);
            Assert.Null(loaded.Grant);
            Assert.Null(loaded.Prompt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); } catch (IOException) { }
            }
        }
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
