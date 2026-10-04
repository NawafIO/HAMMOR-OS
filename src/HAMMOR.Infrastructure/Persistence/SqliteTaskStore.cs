using HAMMOR.Core.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Infrastructure.Persistence;

/// <summary>Durable task storage.</summary>
public sealed class SqliteTaskStore(
    SqliteDatabase database,
    ILogger<SqliteTaskStore> logger) : ITaskStore
{
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

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO tasks
                (id, title, description, state, project_id, created_utc, started_utc,
                 completed_utc, scheduled_for_utc, attempt_count, max_attempts, error, result)
            VALUES
                ($id, $title, $description, $state, $project, $created, $started,
                 $completed, $scheduled, $attempts, $maxAttempts, $error, $result);
            """;

        BindTask(command, task);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

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

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? Map(reader)
            : null;
    }

    public async Task<HammorTask> UpdateAsync(
        HammorTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

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
                result = $result
            WHERE id = $id;
            """;

        BindTask(command, task);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected == 0)
        {
            throw new InvalidOperationException($"No task with id '{task.Id}' exists to update.");
        }

        TaskChanged?.Invoke(this, task);
        return task;
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

        var tasks = new List<HammorTask>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tasks.Add(Map(reader));
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
        + "completed_utc, scheduled_for_utc, attempt_count, max_attempts, error, result";

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
    }

    private static HammorTask Map(SqliteDataReader reader) => new()
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
    };
}
