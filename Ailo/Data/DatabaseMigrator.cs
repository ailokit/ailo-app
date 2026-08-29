using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Ailo.Data;

/// <summary>Applies embedded SQLite migrations exactly once and in version order.</summary>
public sealed class DatabaseMigrator
{
    private const int LatestVersion = 12;
    private readonly SqliteDatabase _database;

    public DatabaseMigrator(SqliteDatabase database) => _database = database;

    /// <summary>Brings the local database schema up to the version shipped by this application build.</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Version INTEGER NOT NULL PRIMARY KEY,
                AppliedAt TEXT NOT NULL
            );
            """, cancellationToken).ConfigureAwait(false);

        for (var version = 1; version <= LatestVersion; version++)
        {
            if (await IsAppliedAsync(connection, transaction, version, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var skipExistingSchema = version == 5
                && await HasColumnAsync(connection, transaction, "Messages", "Reasoning", cancellationToken).ConfigureAwait(false);
            var skipCronIdMigration = version == 10
                && await HasIntegerPrimaryKeyAsync(connection, transaction, "CronJobs", "Id", cancellationToken).ConfigureAwait(false);
            if (!skipExistingSchema && !skipCronIdMigration)
            {
                await ExecuteAsync(connection, transaction, await ReadMigrationAsync(version, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            }

            await ExecuteAsync(connection, transaction, "INSERT INTO SchemaMigrations (Version, AppliedAt) VALUES ($version, $appliedAt);", cancellationToken, ("$version", version), ("$appliedAt", DateTimeOffset.UtcNow.ToString("O"))).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsAppliedAsync(SqliteConnection connection, SqliteTransaction transaction, int version, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM SchemaMigrations WHERE Version = $version);";
        command.Parameters.AddWithValue("$version", version);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static async Task<bool> HasColumnAsync(SqliteConnection connection, SqliteTransaction transaction, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM pragma_table_info($table) WHERE name = $column);";
        command.Parameters.AddWithValue("$table", table);
        command.Parameters.AddWithValue("$column", column);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static async Task<bool> HasIntegerPrimaryKeyAsync(SqliteConnection connection, SqliteTransaction transaction, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT type FROM pragma_table_info($table) WHERE name = $column AND pk = 1;";
        command.Parameters.AddWithValue("$table", table);
        command.Parameters.AddWithValue("$column", column);
        var type = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return string.Equals(type?.ToString(), "INTEGER", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadMigrationAsync(int version, CancellationToken cancellationToken)
    {
        var name = version switch
        {
            1 => "InitialSchema",
            2 => "ProviderModels",
            3 => "ProviderModelsMultimodal",
            4 => "MessageReasoning",
            5 => "EnsureMessageReasoning",
            6 => "MergeLegacyReasoning",
            7 => "McpServers",
            8 => "ScheduledNotifications",
            9 => "CronJobs",
            10 => "CronJobsAutoIncrementId",
            11 => "AiloBrandRefresh",
            12 => "EnglishApplicationText",
            _ => throw new InvalidOperationException($"Unknown migration version '{version}'.")
        };
        var resource = $"Ailo.Data.Migrations.{version:0000}_{name}.sql";
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing database migration resource '{resource}'.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
