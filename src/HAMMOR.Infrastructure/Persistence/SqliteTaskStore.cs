using System.Text.Json;
using HAMMOR.Core.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Infrastructure.Persistence;

/// <summary>Durable task storage.</summary>
public sealed class SqliteTaskStore(
    SqliteDatabase database,
    ILogger<SqliteTaskStore> logger,
    TimeProvider? timeProvider = null) : ITaskStore
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private readonly SqliteDatabase _database =
        database ?? throw new ArgumentNullException(nameof(database));

    private readonly ILogger<SqliteTaskStore> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public event EventHandler<HammorTask>? TaskChanged;

    public async Task<HammorTask> CreateAsync(
        HammorTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (task.Grant is not null)
        {
            // A grant exists only to authorise a task that is about to run, and
            // is validated here so no caller can store an out-of-policy grant.
            if (task.State != TaskState.Pending)
            {
                throw new ArgumentException("A task with a grant must be created Pending.", nameof(task));
            }

            if (!string.Equals(task.Grant.TaskId, task.Id, StringComparison.Ordinal))
            {
                throw new ArgumentException("The grant belongs to a different task.", nameof(task));
            }

            if (task.Grant.SupersedesGrantId is not null || task.Grant.SupersededUtc is not null)
            {
                throw new ArgumentException("A new task's first grant cannot supersede another.", nameof(task));
            }

            ThrowIfInvalid(task.Grant);
        }

        await using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO tasks
                    (id, title, description, state, project_id, created_utc, started_utc,
                     completed_utc, scheduled_for_utc, attempt_count, max_attempts, error, result,
                     prompt, blocked_reason, grant_id)
                VALUES
                    ($id, $title, $description, $state, $project, $created, $started,
                     $completed, $scheduled, $attempts, $maxAttempts, $error, $result,
                     $prompt, $blockedReason, $grantId);
                """;

            BindTask(command, task);
            command.Parameters.AddWithValue("$grantId", task.Grant?.Id.OrDbNull() ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (task.Grant is not null)
        {
            await InsertGrantAsync(connection, transaction, task.Grant, cancellationToken)
                .ConfigureAwait(false);
        }

        transaction.Commit();

        TaskChanged?.Invoke(this, task);
        return task;
    }

    public async Task<HammorTask?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        command.CommandText = $"SELECT {Columns} FROM tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        Row? row = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                row = Map(reader);
            }
        }

        return row is null
            ? null
            : await HydrateAsync(connection, row, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HammorTask> UpdateAsync(
        HammorTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        await using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var current = await ReadStateAsync(connection, transaction, task.Id, cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            throw new InvalidOperationException($"No task with id '{task.Id}' exists to update.");
        }

        if (!TaskStateMachine.IsAllowed(current.Value.State, task.State))
        {
            throw new InvalidOperationException(
                $"Task state change {current.Value.State} -> {task.State} is not allowed."
                + (current.Value.State == TaskState.Blocked
                    ? " A Blocked task can only resume with a new grant (ResumeBlockedAsync)."
                    : string.Empty));
        }

        // Grants are immutable: UpdateAsync never writes grant_id, and refuses a
        // task object that carries a different grant than the one stored.
        if (!string.Equals(task.Grant?.Id, current.Value.GrantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A task's grant is immutable and cannot be changed through UpdateAsync.");
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE tasks SET
                    title = $title,
                    description = $description,
                    state = $state,
                    project_id = $project,
                    created_utc = $created,
                    started_utc = $started,
                    completed_utc = $completed,
                    scheduled_for_utc = $scheduled,
                    attempt_count = $attempts,
                    max_attempts = $maxAttempts,
                    error = $error,
                    result = $result,
                    prompt = $prompt,
                    blocked_reason = $blockedReason
                WHERE id = $id;
                """;

            BindTask(command, task);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();

        TaskChanged?.Invoke(this, task);
        return task;
    }

    public async Task<HammorTask> ResumeBlockedAsync(
        string taskId,
        TaskGrant newGrant,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentNullException.ThrowIfNull(newGrant);

        if (!string.Equals(newGrant.TaskId, taskId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The grant belongs to a different task.", nameof(newGrant));
        }

        if (newGrant.SupersededUtc is not null)
        {
            throw new ArgumentException("A superseded grant cannot be used.", nameof(newGrant));
        }

        ThrowIfInvalid(newGrant);

        await using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var current = await ReadStateAsync(connection, transaction, taskId, cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            throw new InvalidOperationException($"No task with id '{taskId}' exists.");
        }

        if (current.Value.State != TaskState.Blocked)
        {
            throw new InvalidOperationException(
                $"Only a Blocked task can be resumed; this task is {current.Value.State}.");
        }

        var oldGrantId = current.Value.GrantId;

        if (string.Equals(newGrant.Id, oldGrantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The blocked grant can never be reused; a new grant is required.");
        }

        if (!string.Equals(newGrant.SupersedesGrantId, oldGrantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The new grant must declare that it supersedes the task's blocked grant.");
        }

        if (oldGrantId is not null)
        {
            await using var retire = connection.CreateCommand();
            retire.Transaction = transaction;
            retire.CommandText = """
                UPDATE task_grants SET superseded_utc = $now
                WHERE id = $id AND superseded_utc IS NULL;
                """;
            retire.Parameters.AddWithValue("$now", _time.GetUtcNow().ToStorage());
            retire.Parameters.AddWithValue("$id", oldGrantId);
            await retire.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await InsertGrantAsync(connection, transaction, newGrant, cancellationToken)
            .ConfigureAwait(false);

        await using (var resume = connection.CreateCommand())
        {
            resume.Transaction = transaction;
            resume.CommandText = """
                UPDATE tasks SET
                    state = $pending,
                    grant_id = $grantId,
                    blocked_reason = NULL,
                    error = NULL,
                    started_utc = NULL,
                    completed_utc = NULL
                WHERE id = $id AND state = $blocked;
                """;
            resume.Parameters.AddWithValue("$pending", (int)TaskState.Pending);
            resume.Parameters.AddWithValue("$blocked", (int)TaskState.Blocked);
            resume.Parameters.AddWithValue("$grantId", newGrant.Id);
            resume.Parameters.AddWithValue("$id", taskId);
            await resume.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();

        var resumed = await GetAsync(taskId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Task '{taskId}' disappeared while resuming.");

        TaskChanged?.Invoke(this, resumed);
        return resumed;
    }

    public async Task<IReadOnlyList<HammorTask>> ListAsync(
        IReadOnlyCollection<TaskState>? states = null,
        string? projectId = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        var filters = new List<string>();

        if (states is { Count: > 0 })
        {
            // Parameterised per value; never string-interpolated from input.
            var names = states.Select((_, i) => $"$state{i}").ToList();
            filters.Add($"state IN ({string.Join(", ", names)})");

            var index = 0;
            foreach (var state in states)
            {
                command.Parameters.AddWithValue($"$state{index++}", (int)state);
            }
        }

        if (!string.IsNullOrWhiteSpace(projectId))
        {
            filters.Add("project_id = $project");
            command.Parameters.AddWithValue("$project", projectId);
        }

        var where = filters.Count > 0 ? $"WHERE {string.Join(" AND ", filters)}" : string.Empty;

        command.CommandText =
            $"SELECT {Columns} FROM tasks {where} ORDER BY created_utc DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));

        var rows = new List<Row>();

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(Map(reader));
            }
        }

        var tasks = new List<HammorTask>(rows.Count);
        foreach (var row in rows)
        {
            tasks.Add(await HydrateAsync(connection, row, cancellationToken).ConfigureAwait(false));
        }

        return tasks;
    }

    public async Task<int> ReconcileInterruptedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        // A Running row at startup means the previous process died mid-task.
        // Marking it Failed is honest; leaving it Running would make the UI
        // claim work is in progress when nothing is executing.
        command.CommandText = """
            UPDATE tasks
            SET state = $failed,
                error = COALESCE(error, 'Interrupted: HAMMOR exited while this task was running.'),
                completed_utc = COALESCE(completed_utc, $now)
            WHERE state = $running;
            """;

        command.Parameters.AddWithValue("$failed", (int)TaskState.Failed);
        command.Parameters.AddWithValue("$running", (int)TaskState.Running);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToStorage());

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (affected > 0)
        {
            _logger.LogWarning(
                "Reconciled {Count} task(s) left Running by a previous session.", affected);
        }

        return affected;
    }

    private const string Columns =
        "id, title, description, state, project_id, created_utc, started_utc, "
        + "completed_utc, scheduled_for_utc, attempt_count, max_attempts, error, result, "
        + "prompt, blocked_reason, grant_id";

    private const string GrantColumns =
        "id, task_id, allowed_tools, max_permission, granted_utc, expires_utc, "
        + "max_tool_calls, project_id, supersedes_grant_id, superseded_utc";

    private sealed record Row(HammorTask Task, string? GrantId);

    private void ThrowIfInvalid(TaskGrant grant)
    {
        var errors = TaskGrantValidator.ValidateStructure(grant, _time.GetUtcNow());
        if (errors.Count > 0)
        {
            throw new ArgumentException("Invalid task grant: " + string.Join(" ", errors));
        }
    }

    private static async Task<(TaskState State, string? GrantId)?> ReadStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string taskId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT state, grant_id FROM tasks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", taskId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ((TaskState)reader.GetInt("state"), reader.GetNullableString("grant_id"));
    }

    private static async Task InsertGrantAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TaskGrant grant,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO task_grants
                (id, task_id, allowed_tools, max_permission, granted_utc, expires_utc,
                 max_tool_calls, project_id, supersedes_grant_id, superseded_utc)
            VALUES
                ($id, $task, $tools, $permission, $granted, $expires,
                 $maxCalls, $project, $supersedes, NULL);
            """;

        command.Parameters.AddWithValue("$id", grant.Id);
        command.Parameters.AddWithValue("$task", grant.TaskId);
        command.Parameters.AddWithValue("$tools", JsonSerializer.Serialize(grant.AllowedTools));
        command.Parameters.AddWithValue("$permission", (int)grant.MaxPermission);
        command.Parameters.AddWithValue("$granted", grant.GrantedUtc.ToStorage());
        command.Parameters.AddWithValue("$expires", grant.ExpiresUtc.ToStorage());
        command.Parameters.AddWithValue("$maxCalls", grant.MaxToolCalls);
        command.Parameters.AddWithValue("$project", grant.ProjectId.OrDbNull());
        command.Parameters.AddWithValue("$supersedes", grant.SupersedesGrantId.OrDbNull());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HammorTask> HydrateAsync(
        SqliteConnection connection,
        Row row,
        CancellationToken cancellationToken)
    {
        if (row.GrantId is null)
        {
            return row.Task;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {GrantColumns} FROM task_grants WHERE id = $id;";
        command.Parameters.AddWithValue("$id", row.GrantId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // A dangling grant id must not silently turn into "no grant".
            throw new InvalidOperationException(
                $"Task '{row.Task.Id}' references missing grant '{row.GrantId}'.");
        }

        var tools = JsonSerializer.Deserialize<List<string>>(reader.GetStringValue("allowed_tools"))
            ?? new List<string>();

        var grant = new TaskGrant
        {
            Id = reader.GetStringValue("id"),
            TaskId = reader.GetStringValue("task_id"),
            AllowedTools = tools,
            MaxPermission = (HAMMOR.Core.Tools.ToolPermission)reader.GetInt("max_permission"),
            GrantedUtc = reader.GetTimestamp("granted_utc"),
            ExpiresUtc = reader.GetTimestamp("expires_utc"),
            MaxToolCalls = reader.GetInt("max_tool_calls"),
            ProjectId = reader.GetNullableString("project_id"),
            SupersedesGrantId = reader.GetNullableString("supersedes_grant_id"),
            SupersededUtc = reader.GetNullableTimestamp("superseded_utc"),
        };

        return row.Task with { Grant = grant };
    }

    private static void BindTask(SqliteCommand command, HammorTask task)
    {
        command.Parameters.AddWithValue("$id", task.Id);
        command.Parameters.AddWithValue("$title", task.Title);
        command.Parameters.AddWithValue("$description", task.Description.OrDbNull());
        command.Parameters.AddWithValue("$state", (int)task.State);
        command.Parameters.AddWithValue("$project", task.ProjectId.OrDbNull());
        command.Parameters.AddWithValue("$created", task.CreatedUtc.ToStorage());
        command.Parameters.AddWithValue("$started", task.StartedUtc.ToStorageOrNull());
        command.Parameters.AddWithValue("$completed", task.CompletedUtc.ToStorageOrNull());
        command.Parameters.AddWithValue("$scheduled", task.ScheduledForUtc.ToStorageOrNull());
        command.Parameters.AddWithValue("$attempts", task.AttemptCount);
        command.Parameters.AddWithValue("$maxAttempts", task.MaxAttempts);
        command.Parameters.AddWithValue("$error", task.Error.OrDbNull());
        command.Parameters.AddWithValue("$result", task.Result.OrDbNull());
        command.Parameters.AddWithValue("$prompt", task.Prompt.OrDbNull());
        command.Parameters.AddWithValue("$blockedReason", task.BlockedReason.OrDbNull());
    }

    private static Row Map(SqliteDataReader reader) => new(new HammorTask
    {
        Id = reader.GetStringValue("id"),
        Title = reader.GetStringValue("title"),
        Description = reader.GetNullableString("description"),
        State = (TaskState)reader.GetInt("state"),
        ProjectId = reader.GetNullableString("project_id"),
        CreatedUtc = reader.GetTimestamp("created_utc"),
        StartedUtc = reader.GetNullableTimestamp("started_utc"),
        CompletedUtc = reader.GetNullableTimestamp("completed_utc"),
        ScheduledForUtc = reader.GetNullableTimestamp("scheduled_for_utc"),
        AttemptCount = reader.GetInt("attempt_count"),
        MaxAttempts = reader.GetInt("max_attempts"),
        Error = reader.GetNullableString("error"),
        Result = reader.GetNullableString("result"),
        Prompt = reader.GetNullableString("prompt"),
        BlockedReason = reader.GetNullableString("blocked_reason"),
    }, reader.GetNullableString("grant_id"));
}
