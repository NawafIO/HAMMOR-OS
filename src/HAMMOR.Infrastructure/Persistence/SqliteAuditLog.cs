using HAMMOR.Core.Audit;
using HAMMOR.Core.Diagnostics;
using HAMMOR.Core.Tools;
using Microsoft.Data.Sqlite;

namespace HAMMOR.Infrastructure.Persistence;

/// <summary>
/// Append-only audit trail in SQLite. Every message is passed through
/// <see cref="SecretRedactor"/> on the way in, so a credential that leaked
/// into an exception message cannot be persisted verbatim.
/// </summary>
public sealed class SqliteAuditLog(SqliteDatabase database) : IAuditLog
{
    private readonly SqliteDatabase _database =
        database ?? throw new ArgumentNullException(nameof(database));

    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO audit
                (id, timestamp_utc, category, subject, message, outcome,
                 permission, project_id, correlation_id)
            VALUES
                ($id, $timestamp, $category, $subject, $message, $outcome,
                 $permission, $project, $correlation);
            """;

        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$timestamp", entry.TimestampUtc.ToStorage());
        command.Parameters.AddWithValue("$category", (int)entry.Category);
        command.Parameters.AddWithValue("$subject", entry.Subject);
        command.Parameters.AddWithValue("$message", SecretRedactor.Redact(entry.Message));
        command.Parameters.AddWithValue("$outcome", (int)entry.Outcome);
        command.Parameters.AddWithValue(
            "$permission",
            entry.Permission is null ? DBNull.Value : (int)entry.Permission.Value);
        command.Parameters.AddWithValue("$project", entry.ProjectId.OrDbNull());
        command.Parameters.AddWithValue("$correlation", entry.CorrelationId.OrDbNull());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetRecentAsync(
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT id, timestamp_utc, category, subject, message, outcome,
                   permission, project_id, correlation_id
            FROM audit
            ORDER BY timestamp_utc DESC
            LIMIT $limit;
            """;

        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));

        var entries = new List<AuditEntry>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var permissionIndex = reader.GetOrdinal("permission");

            entries.Add(new AuditEntry
            {
                Id = reader.GetStringValue("id"),
                TimestampUtc = reader.GetTimestamp("timestamp_utc"),
                Category = (AuditCategory)reader.GetInt("category"),
                Subject = reader.GetStringValue("subject"),
                Message = reader.GetStringValue("message"),
                Outcome = (AuditOutcome)reader.GetInt("outcome"),
                Permission = reader.IsDBNull(permissionIndex)
                    ? null
                    : (ToolPermission)reader.GetInt32(permissionIndex),
                ProjectId = reader.GetNullableString("project_id"),
                CorrelationId = reader.GetNullableString("correlation_id"),
            });
        }

        return entries;
    }
}
