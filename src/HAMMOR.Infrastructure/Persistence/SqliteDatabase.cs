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

        // Unattended task execution (ADR-003). Columns are added in place so a
        // database created by an earlier phase keeps its rows; existing tasks
        // get no grant and therefore remain interactive-only.
        EnsureColumn(connection, "tasks", "prompt", "TEXT NULL");
        EnsureColumn(connection, "tasks", "blocked_reason", "TEXT NULL");
        EnsureColumn(connection, "tasks", "grant_id", "TEXT NULL");

        using var grants = connection.CreateCommand();
        grants.CommandText = TaskGrantSchema;
        grants.ExecuteNonQuery();

        _logger.LogInformation("SQLite schema verified.");
    }

    // Grants are append-only: no row is edited or deleted, apart from the
    // one-way superseded_utc stamp set when a Blocked task resumes under a new
    // grant. Triggers enforce that below the application layer too.
    private const string TaskGrantSchema = """
        CREATE TABLE IF NOT EXISTS task_grants (
            id                  TEXT PRIMARY KEY,
            task_id             TEXT NOT NULL,
            allowed_tools       TEXT NOT NULL,
            max_permission      INTEGER NOT NULL,
            granted_utc         TEXT NOT NULL,
            expires_utc         TEXT NOT NULL,
            max_tool_calls      INTEGER NOT NULL,
            project_id          TEXT NULL,
            supersedes_grant_id TEXT NULL,
            superseded_utc      TEXT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_task_grants_task ON task_grants(task_id);

        CREATE TRIGGER IF NOT EXISTS trg_task_grants_immutable
        BEFORE UPDATE ON task_grants
        WHEN OLD.id <> NEW.id
          OR OLD.task_id <> NEW.task_id
          OR OLD.allowed_tools <> NEW.allowed_tools
          OR OLD.max_permission <> NEW.max_permission
          OR OLD.granted_utc <> NEW.granted_utc
          OR OLD.expires_utc <> NEW.expires_utc
          OR OLD.max_tool_calls <> NEW.max_tool_calls
          OR IFNULL(OLD.project_id, '') <> IFNULL(NEW.project_id, '')
          OR IFNULL(OLD.supersedes_grant_id, '') <> IFNULL(NEW.supersedes_grant_id, '')
          OR (OLD.superseded_utc IS NOT NULL
              AND IFNULL(NEW.superseded_utc, '') <> OLD.superseded_utc)
        BEGIN
            SELECT RAISE(ABORT, 'task grants are immutable');
        END;

        CREATE TRIGGER IF NOT EXISTS trg_task_grants_no_delete
        BEFORE DELETE ON task_grants
        BEGIN
            SELECT RAISE(ABORT, 'task grants are append-only');
        END;

        CREATE TRIGGER IF NOT EXISTS trg_tasks_grant_change
        BEFORE UPDATE OF grant_id ON tasks
        WHEN IFNULL(OLD.grant_id, '') <> IFNULL(NEW.grant_id, '')
         AND NOT (OLD.state = 5 AND NEW.state = 0)
        BEGIN
            SELECT RAISE(ABORT, 'a task grant can only change when a Blocked task resumes');
        END;
        """;

    private static void EnsureColumn(
        SqliteConnection connection,
        string table,
        string column,
        string definition)
    {
        // table/column/definition are compile-time constants, never input.
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $name;";
        check.Parameters.AddWithValue("$name", column);

        if (Convert.ToInt64(check.ExecuteScalar()) > 0)
        {
            return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }
}
