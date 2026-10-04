using HAMMOR.Core.Projects;
using Microsoft.Data.Sqlite;

namespace HAMMOR.Infrastructure.Persistence;

/// <summary>Project storage.</summary>
public sealed class SqliteProjectStore(SqliteDatabase database) : IProjectStore
{
    private const string Columns =
        "id, name, description, root_path, kind, context, created_utc, modified_utc, "
        + "last_opened_utc, is_archived";

    private readonly SqliteDatabase _database =
        database ?? throw new ArgumentNullException(nameof(database));

    public async Task<HammorProject> CreateAsync(
        HammorProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO projects
                (id, name, description, root_path, kind, context, created_utc,
                 modified_utc, last_opened_utc, is_archived)
            VALUES
                ($id, $name, $description, $rootPath, $kind, $context, $created,
                 $modified, $lastOpened, $archived);
            """;

        Bind(command, project);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return project;
    }

    public async Task<HammorProject?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        command.CommandText = $"SELECT {Columns} FROM projects WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? Map(reader)
            : null;
    }

    public async Task<HammorProject> UpdateAsync(
        HammorProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE projects SET
                name = $name,
                description = $description,
                root_path = $rootPath,
                kind = $kind,
                context = $context,
                created_utc = $created,
                modified_utc = $modified,
                last_opened_utc = $lastOpened,
                is_archived = $archived
            WHERE id = $id;
            """;

        Bind(command, project);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected == 0)
        {
            throw new InvalidOperationException(
                $"No project with id '{project.Id}' exists to update.");
        }

        return project;
    }

    public async Task<IReadOnlyList<HammorProject>> ListAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT {Columns} FROM projects
            {(includeArchived ? string.Empty : "WHERE is_archived = 0")}
            ORDER BY COALESCE(last_opened_utc, modified_utc) DESC;
            """;

        var projects = new List<HammorProject>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            projects.Add(Map(reader));
        }

        return projects;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.OpenConnection();
        await using var command = connection.CreateCommand();

        command.CommandText = "DELETE FROM projects WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Bind(SqliteCommand command, HammorProject project)
    {
        command.Parameters.AddWithValue("$id", project.Id);
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$description", project.Description.OrDbNull());
        command.Parameters.AddWithValue("$rootPath", project.RootPath.OrDbNull());
        command.Parameters.AddWithValue("$kind", (int)project.Kind);
        command.Parameters.AddWithValue("$context", project.Context.OrDbNull());
        command.Parameters.AddWithValue("$created", project.CreatedUtc.ToStorage());
        command.Parameters.AddWithValue("$modified", project.ModifiedUtc.ToStorage());
        command.Parameters.AddWithValue("$lastOpened", project.LastOpenedUtc.ToStorageOrNull());
        command.Parameters.AddWithValue("$archived", project.IsArchived ? 1 : 0);
    }

    private static HammorProject Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetStringValue("id"),
        Name = reader.GetStringValue("name"),
        Description = reader.GetNullableString("description"),
        RootPath = reader.GetNullableString("root_path"),
        Kind = (ProjectKind)reader.GetInt("kind"),
        Context = reader.GetNullableString("context"),
        CreatedUtc = reader.GetTimestamp("created_utc"),
        ModifiedUtc = reader.GetTimestamp("modified_utc"),
        LastOpenedUtc = reader.GetNullableTimestamp("last_opened_utc"),
        IsArchived = reader.GetBool("is_archived"),
    };
}
