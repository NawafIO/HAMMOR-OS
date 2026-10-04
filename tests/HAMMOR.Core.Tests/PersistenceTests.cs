using HAMMOR.Core.Audit;
using HAMMOR.Core.Memory;
using HAMMOR.Core.Storage;
using HAMMOR.Core.Tasks;
using HAMMOR.Core.Tools;
using HAMMOR.Infrastructure.Memory;
using HAMMOR.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HAMMOR.Core.Tests;

/// <summary>
/// Exercises the SQLite stores against a real temporary database, so the
/// schema and the mapping code are both verified rather than mocked.
/// </summary>
public sealed class PersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "hammor-tests", Guid.NewGuid().ToString("n"));

    private readonly HammorPaths _paths;
    private readonly SqliteDatabase _database;

    public PersistenceTests()
    {
        _paths = new HammorPaths(_root);
        _database = new SqliteDatabase(_paths, NullLogger<SqliteDatabase>.Instance);
        _database.Migrate();
    }

    [Fact]
    public void Migrate_is_idempotent()
    {
        // Runs on every launch, so a second call must not fail.
        _database.Migrate();
        _database.Migrate();
    }

    // ----------------------------- Tasks -----------------------------

    [Fact]
    public async Task Tasks_round_trip_through_sqlite()
    {
        var store = CreateTaskStore();

        var created = await store.CreateAsync(new HammorTask
        {
            Title = "Index the project",
            Description = "Walk the repository and summarise it.",
            MaxAttempts = 3,
        });

        var loaded = await store.GetAsync(created.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Index the project", loaded!.Title);
        Assert.Equal(TaskState.Pending, loaded.State);
        Assert.Equal(3, loaded.MaxAttempts);
    }

    [Fact]
    public async Task Task_updates_persist()
    {
        var store = CreateTaskStore();
        var created = await store.CreateAsync(new HammorTask { Title = "Work" });

        await store.UpdateAsync(created with
        {
            State = TaskState.Completed,
            Result = "Done.",
            CompletedUtc = DateTimeOffset.UtcNow,
        });

        var loaded = await store.GetAsync(created.Id);

        Assert.Equal(TaskState.Completed, loaded!.State);
        Assert.Equal("Done.", loaded.Result);
        Assert.True(loaded.IsTerminal);
    }

    [Fact]
    public async Task Updating_a_missing_task_throws_rather_than_silently_doing_nothing()
    {
        var store = CreateTaskStore();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.UpdateAsync(new HammorTask { Title = "Ghost" }));
    }

    [Fact]
    public async Task Tasks_can_be_filtered_by_state()
    {
        var store = CreateTaskStore();

        var pending = await store.CreateAsync(new HammorTask { Title = "Pending" });
        var running = await store.CreateAsync(new HammorTask { Title = "Running" });
        await store.UpdateAsync(running with { State = TaskState.Running });

        var onlyRunning = await store.ListAsync([TaskState.Running]);

        Assert.Single(onlyRunning);
        Assert.Equal("Running", onlyRunning[0].Title);
        Assert.DoesNotContain(onlyRunning, t => t.Id == pending.Id);
    }

    /// <summary>
    /// Crash recovery: a task left Running by a killed process must not keep
    /// claiming to be in progress.
    /// </summary>
    [Fact]
    public async Task Interrupted_running_tasks_are_reconciled_to_failed()
    {
        var store = CreateTaskStore();
        var task = await store.CreateAsync(new HammorTask { Title = "Interrupted" });
        await store.UpdateAsync(task with { State = TaskState.Running });

        var reconciled = await store.ReconcileInterruptedAsync();

        Assert.Equal(1, reconciled);

        var loaded = await store.GetAsync(task.Id);
        Assert.Equal(TaskState.Failed, loaded!.State);
        Assert.False(string.IsNullOrWhiteSpace(loaded.Error));
        Assert.NotNull(loaded.CompletedUtc);
    }

    [Fact]
    public async Task Reconcile_leaves_terminal_tasks_alone()
    {
        var store = CreateTaskStore();
        var done = await store.CreateAsync(new HammorTask { Title = "Finished" });
        await store.UpdateAsync(done with { State = TaskState.Completed, Result = "ok" });

        var reconciled = await store.ReconcileInterruptedAsync();

        Assert.Equal(0, reconciled);
        Assert.Equal(TaskState.Completed, (await store.GetAsync(done.Id))!.State);
    }

    // ---------------------------- Memory -----------------------------

    [Fact]
    public async Task Memory_entries_round_trip_and_are_searchable()
    {
        var store = CreateMemoryStore();

        await store.SaveAsync(new MemoryEntry
        {
            Title = "Preferred editor",
            Content = "The user prefers Visual Studio over VS Code for C#.",
            Kind = MemoryKind.Preference,
        });

        var results = await store.SearchAsync("Visual Studio");

        Assert.Single(results);
        Assert.Equal("Preferred editor", results[0].Title);
        Assert.Equal(MemoryKind.Preference, results[0].Kind);
    }

    [Fact]
    public async Task Memory_search_escapes_like_wildcards()
    {
        var store = CreateMemoryStore();

        await store.SaveAsync(new MemoryEntry { Title = "Literal", Content = "100% certain" });
        await store.SaveAsync(new MemoryEntry { Title = "Other", Content = "nothing here" });

        // A bare % would match everything if wildcards were not escaped.
        var results = await store.SearchAsync("100%");

        Assert.Single(results);
        Assert.Equal("Literal", results[0].Title);
    }

    [Fact]
    public async Task Memory_save_writes_a_markdown_mirror_and_records_the_hash()
    {
        var store = CreateMirroringMemoryStore();

        var saved = await store.SaveAsync(new MemoryEntry
        {
            Title = "Mirrored note",
            Content = "This should also exist as a readable markdown file.",
        });

        Assert.NotNull(saved.MarkdownPath);
        Assert.NotNull(saved.ContentHash);

        var fullPath = Path.Combine(_paths.MemoryDirectory, saved.MarkdownPath!);
        Assert.True(File.Exists(fullPath));

        var markdown = await File.ReadAllTextAsync(fullPath);
        Assert.Contains("Mirrored note", markdown, StringComparison.Ordinal);
        Assert.Contains("readable markdown file", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Memory_save_is_an_upsert_on_id()
    {
        var store = CreateMemoryStore();
        var entry = new MemoryEntry { Title = "First", Content = "v1" };

        await store.SaveAsync(entry);
        await store.SaveAsync(entry with { Title = "Second", Content = "v2" });

        var loaded = await store.GetAsync(entry.Id);

        Assert.Equal("Second", loaded!.Title);
        Assert.Equal("v2", loaded.Content);
        Assert.Single(await store.GetRecentAsync());
    }

    [Fact]
    public async Task Deleting_memory_removes_the_markdown_mirror_too()
    {
        var store = CreateMirroringMemoryStore();
        var saved = await store.SaveAsync(new MemoryEntry { Title = "Temp", Content = "x" });
        var fullPath = Path.Combine(_paths.MemoryDirectory, saved.MarkdownPath!);

        await store.DeleteAsync(saved.Id);

        Assert.Null(await store.GetAsync(saved.Id));
        Assert.False(File.Exists(fullPath));
    }

    // -------------------- Incremental indexing -----------------------

    /// <summary>
    /// The second pass must do no work when nothing changed — this is the
    /// behaviour that keeps startup cost flat as memory grows.
    /// </summary>
    [Fact]
    public async Task Reindexing_unchanged_files_reports_no_changes()
    {
        var store = CreateMirroringMemoryStore();
        await store.SaveAsync(new MemoryEntry { Title = "Indexed", Content = "body" });

        var indexer = CreateIndexer();

        var first = await indexer.ReindexAsync();
        Assert.Equal(1, first.Scanned);
        Assert.Equal(1, first.Added);
        Assert.True(first.AnyChanges);

        var second = await indexer.ReindexAsync();
        Assert.Equal(1, second.Scanned);
        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Updated);
        Assert.False(second.AnyChanges);
    }

    [Fact]
    public async Task Reindexing_detects_changed_content()
    {
        var store = CreateMirroringMemoryStore();
        var saved = await store.SaveAsync(new MemoryEntry { Title = "Changing", Content = "v1" });
        var fullPath = Path.Combine(_paths.MemoryDirectory, saved.MarkdownPath!);

        var indexer = CreateIndexer();
        await indexer.ReindexAsync();

        await File.WriteAllTextAsync(fullPath, "completely different content, different length");

        var report = await indexer.ReindexAsync();

        Assert.Equal(1, report.Updated);
    }

    [Fact]
    public async Task Reindexing_drops_entries_for_deleted_files()
    {
        var store = CreateMirroringMemoryStore();
        var saved = await store.SaveAsync(new MemoryEntry { Title = "Doomed", Content = "x" });
        var fullPath = Path.Combine(_paths.MemoryDirectory, saved.MarkdownPath!);

        var indexer = CreateIndexer();
        await indexer.ReindexAsync();

        File.Delete(fullPath);

        var report = await indexer.ReindexAsync();

        Assert.Equal(0, report.Scanned);
        Assert.Equal(1, report.Removed);
    }

    // ----------------------------- Audit -----------------------------

    [Fact]
    public async Task Audit_entries_are_persisted_newest_first()
    {
        var log = new SqliteAuditLog(_database);

        await log.AppendAsync(new AuditEntry
        {
            Category = AuditCategory.Authorisation,
            Subject = "memory.save",
            Message = "Older entry",
            Outcome = AuditOutcome.Allowed,
            Permission = ToolPermission.Write,
            TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
        });

        await log.AppendAsync(new AuditEntry
        {
            Category = AuditCategory.ToolExecution,
            Subject = "memory.search",
            Message = "Newer entry",
            Outcome = AuditOutcome.Succeeded,
        });

        var entries = await log.GetRecentAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal("Newer entry", entries[0].Message);
        Assert.Equal(ToolPermission.Write, entries[1].Permission);
    }

    /// <summary>
    /// Credentials that leak into an exception message must not reach the
    /// audit table verbatim.
    /// </summary>
    [Fact]
    public async Task Audit_messages_are_redacted_on_write()
    {
        var log = new SqliteAuditLog(_database);
        const string key = "sk-ant-api03-LeakedKeyValue1234567890";

        await log.AppendAsync(new AuditEntry
        {
            Category = AuditCategory.Provider,
            Subject = "claude",
            Message = $"Request failed using {key}",
            Outcome = AuditOutcome.Failed,
        });

        var entries = await log.GetRecentAsync();

        Assert.DoesNotContain(key, entries[0].Message, StringComparison.Ordinal);
    }

    // --------------------------- Helpers -----------------------------

    private SqliteTaskStore CreateTaskStore() =>
        new(_database, NullLogger<SqliteTaskStore>.Instance);

    private SqliteMemoryStore CreateMemoryStore() => new(_database);

    private MarkdownMirroringMemoryStore CreateMirroringMemoryStore() =>
        new(CreateMemoryStore(),
            new MarkdownMemoryMirror(_paths, NullLogger<MarkdownMemoryMirror>.Instance));

    private MarkdownMemoryIndexer CreateIndexer() =>
        new(_database, _paths, NullLogger<MarkdownMemoryIndexer>.Instance);

    public void Dispose()
    {
        // Release pooled SQLite handles so the temp folder can be deleted.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A lingering file handle on a CI agent should not fail an
                // otherwise-passing test run.
            }
        }
    }
}
