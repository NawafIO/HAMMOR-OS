using HAMMOR.Core.Storage;
using HAMMOR.Core.Tasks;
using HAMMOR.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HAMMOR.Core.Tests.Tasks;

/// <summary>ADR-004: grant roots persistence, immutability and migration from Phase 5.</summary>
public sealed class GrantRootsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hammor-tests", Guid.NewGuid().ToString("n"));
    private readonly TestClock _clock = new(TestTasks.Start);

    private (SqliteDatabase Database, SqliteTaskStore Store) Open(string? root = null)
    {
        var database = new SqliteDatabase(new HammorPaths(root ?? _root), NullLogger<SqliteDatabase>.Instance);
        database.Migrate();
        return (database, new SqliteTaskStore(database, NullLogger<SqliteTaskStore>.Instance, _clock));
    }

    [Fact]
    public async Task Roots_round_trip_with_the_grant()
    {
        var (_, store) = Open();
        var task = TestTasks.Task(_clock);
        var roots = new[] { @"C:\Work\Repo\", @"D:\Notes\" };
        task = task with { Grant = task.Grant! with { AllowedRoots = roots } };

        await store.CreateAsync(task);
        var loaded = await store.GetAsync(task.Id);

        Assert.Equal(roots, loaded!.Grant!.AllowedRoots);
    }

    [Fact]
    public async Task Grant_without_roots_loads_as_empty()
    {
        var (_, store) = Open();
        var task = await store.CreateAsync(TestTasks.Task(_clock));

        Assert.Empty((await store.GetAsync(task.Id))!.Grant!.AllowedRoots);
    }

    [Fact]
    public async Task Roots_cannot_be_edited_in_the_database()
    {
        var (database, store) = Open();
        var task = TestTasks.Task(_clock);
        task = task with { Grant = task.Grant! with { AllowedRoots = new[] { @"C:\Work\Repo\" } } };
        await store.CreateAsync(task);

        using var connection = database.OpenConnection();
        using var widen = connection.CreateCommand();
        widen.CommandText = "UPDATE task_grants SET allowed_roots = '[\"C:\\\\\"]' WHERE id = $id;";
        widen.Parameters.AddWithValue("$id", task.Grant!.Id);

        Assert.Throws<SqliteException>(() => widen.ExecuteNonQuery());

        using var clear = connection.CreateCommand();
        clear.CommandText = "UPDATE task_grants SET allowed_roots = NULL WHERE id = $id;";
        clear.Parameters.AddWithValue("$id", task.Grant!.Id);

        Assert.Throws<SqliteException>(() => clear.ExecuteNonQuery());
        Assert.Equal(new[] { @"C:\Work\Repo\" }, (await store.GetAsync(task.Id))!.Grant!.AllowedRoots);
    }

    [Fact]
    public async Task Granted_task_must_allow_between_1_and_10_attempts()
    {
        var (_, store) = Open();

        foreach (var attempts in new[] { 0, -1, HammorTask.MaxAttemptsLimit + 1 })
        {
            var task = TestTasks.Task(_clock) with { MaxAttempts = attempts };
            await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(task));
            Assert.Null(await store.GetAsync(task.Id));
        }

        var atLimit = await store.CreateAsync(TestTasks.Task(_clock) with { MaxAttempts = HammorTask.MaxAttemptsLimit });
        Assert.Equal(HammorTask.MaxAttemptsLimit, (await store.GetAsync(atLimit.Id))!.MaxAttempts);
    }

    [Fact]
    public async Task Resume_stores_the_new_grants_roots()
    {
        var (_, store) = Open();
        var created = await store.CreateAsync(TestTasks.Task(_clock));
        var running = await store.UpdateAsync(created with { State = TaskState.Running });
        var blocked = await store.UpdateAsync(running with { State = TaskState.Blocked });

        var next = TestTasks.Grant(blocked.Id, _clock, supersedes: blocked.Grant!.Id) with { AllowedRoots = new[] { @"C:\Work\Repo\" } };
        var resumed = await store.ResumeBlockedAsync(blocked.Id, next);

        Assert.Equal(new[] { @"C:\Work\Repo\" }, resumed.Grant!.AllowedRoots);
    }

    [Fact]
    public async Task Migration_from_a_phase_5_database_keeps_existing_grants_with_no_roots()
    {
        var root = Path.Combine(Path.GetTempPath(), "hammor-tests", Guid.NewGuid().ToString("n"));
        try
        {
            var paths = new HammorPaths(root);
            paths.EnsureCreated();

            // The task tables exactly as Phase 5 created them: no allowed_roots column, no roots trigger.
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
                        error TEXT NULL, result TEXT NULL, prompt TEXT NULL, blocked_reason TEXT NULL,
                        grant_id TEXT NULL);
                    CREATE TABLE task_grants (
                        id TEXT PRIMARY KEY, task_id TEXT NOT NULL, allowed_tools TEXT NOT NULL,
                        max_permission INTEGER NOT NULL, granted_utc TEXT NOT NULL, expires_utc TEXT NOT NULL,
                        max_tool_calls INTEGER NOT NULL, project_id TEXT NULL, supersedes_grant_id TEXT NULL,
                        superseded_utc TEXT NULL);
                    INSERT INTO tasks (id, title, state, created_utc, prompt, grant_id)
                        VALUES ('p5', 'Phase 5 task', 0, '2026-10-05T12:00:00.0000000+00:00', 'do it', 'g5');
                    INSERT INTO task_grants
                        (id, task_id, allowed_tools, max_permission, granted_utc, expires_utc, max_tool_calls)
                        VALUES ('g5', 'p5', '["memory.search"]', 0,
                                '2026-10-05T12:00:00.0000000+00:00', '2026-10-06T12:00:00.0000000+00:00', 3);
                    """;
                create.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();

            var (database, store) = Open(root);
            database.Migrate(); // idempotent

            var loaded = await store.GetAsync("p5");
            Assert.NotNull(loaded!.Grant);
            Assert.Equal("g5", loaded.Grant!.Id);
            Assert.Equal(new[] { "memory.search" }, loaded.Grant.AllowedTools);
            Assert.Empty(loaded.Grant.AllowedRoots);

            // The new trigger now protects the migrated row as well.
            using var connection = database.OpenConnection();
            using var widen = connection.CreateCommand();
            widen.CommandText = "UPDATE task_grants SET allowed_roots = '[\"C:\\\\\"]' WHERE id = 'g5';";
            Assert.Throws<SqliteException>(() => widen.ExecuteNonQuery());
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
