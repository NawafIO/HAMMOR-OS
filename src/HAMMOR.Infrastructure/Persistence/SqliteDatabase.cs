using HAMMOR.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Infrastructure.Persistence;

/// <summary>
/// Owns the SQLite connection string and schema. Created once at startup;
/// every store opens short-lived connections against it.
/// </summary>
public sealed class SqliteDatabase
{
    private readonly ILogger<SqliteDatabase> _logger;

    public SqliteDatabase(HammorPaths paths, ILogger<SqliteDatabase> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        paths.EnsureCreated();

        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Shared cache + WAL lets the UI read while a background task writes.
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public string ConnectionString { get; }

    /// <summary>Opens and returns an open connection. Caller disposes.</summary>
    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    /// <summary>
    /// Creates tables and indexes when absent. Safe to run on every launch.
    /// </summary>
    public void Migrate()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            CREATE TABLE IF NOT EXISTS projects (
                id              TEXT PRIMARY KEY,
                name            TEXT NOT NULL,
                description     TEXT NULL,
                root_path       TEXT NULL,
                kind            INTEGER NOT NULL DEFAULT 0,
                context         TEXT NULL,
                created_utc     TEXT NOT NULL,
                modified_utc    TEXT NOT NULL,
                last_opened_utc TEXT NULL,
                is_archived     INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS tasks (
                id                TEXT PRIMARY KEY,
                title             TEXT NOT NULL,
                description       TEXT NULL,
                state             INTEGER NOT NULL,
                project_id        TEXT NULL REFERENCES projects(id) ON DELETE SET NULL,
                created_utc       TEXT NOT NULL,
                started_utc       TEXT NULL,
                completed_utc     TEXT NULL,
                scheduled_for_utc TEXT NULL,
                attempt_count     INTEGER NOT NULL DEFAULT 0,
                max_attempts      INTEGER NOT NULL DEFAULT 1,
                error             TEXT NULL,
                result            TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_tasks_state ON tasks(state, created_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_tasks_project ON tasks(project_id);

            CREATE TABLE IF NOT EXISTS memory (
                id            TEXT PRIMARY KEY,
                title         TEXT NOT NULL,
                content       TEXT NOT NULL,
                project_id    TEXT NULL REFERENCES projects(id) ON DELETE SET NULL,
                task_id       TEXT NULL,
                kind          INTEGER NOT NULL DEFAULT 0,
                created_utc   TEXT NOT NULL,
                modified_utc  TEXT NOT NULL,
                markdown_path TEXT NULL,
                content_hash  TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_memory_created ON memory(created_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_memory_project ON memory(project_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_memory_markdown ON memory(markdown_path)
                WHERE markdown_path IS NOT NULL;

            CREATE TABLE IF NOT EXISTS audit (
                id             TEXT PRIMARY KEY,
                timestamp_utc  TEXT NOT NULL,
                category       INTEGER NOT NULL,
                subject        TEXT NOT NULL,
                message        TEXT NOT NULL,
                outcome        INTEGER NOT NULL,
                permission     INTEGER NULL,
                project_id     TEXT NULL,
                correlation_id TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_audit_time ON audit(timestamp_utc DESC);

            -- Tracks which markdown files have been indexed, so startup
            -- indexing can skip unchanged files instead of re-reading the
            -- whole memory folder.
            CREATE TABLE IF NOT EXISTS markdown_index (
                relative_path  TEXT PRIMARY KEY,
                size_bytes     INTEGER NOT NULL,
                modified_utc   TEXT NOT NULL,
                content_hash   TEXT NOT NULL,
                memory_id      TEXT NULL,
                indexed_utc    TEXT NOT NULL
            );
            """;

        command.ExecuteNonQuery();
        _logger.LogInformation("SQLite schema verified.");
    }
}
