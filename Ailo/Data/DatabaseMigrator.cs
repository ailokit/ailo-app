using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Ailo.Data;

/// <summary>Applies the versioned schema migrations shipped with this version of Ailo.</summary>
public sealed class DatabaseMigrator(SqliteDatabase database)
{
    private static readonly Regex MigrationResourcePattern = new(
        @"^Ailo\.Data\.Migrations\.(?<version>\d{4})_.+\.sql$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Applies each embedded migration exactly once, in version order.</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        var migrations = GetMigrations();
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Version INTEGER NOT NULL PRIMARY KEY,
                AppliedAt TEXT NOT NULL
            );
            """, cancellationToken).ConfigureAwait(false);

        foreach (var migration in migrations)
        {
            if (await IsAppliedAsync(connection, transaction, migration.Version, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await ExecuteAsync(connection, transaction, await ReadMigrationAsync(migration.ResourceName, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO SchemaMigrations (Version, AppliedAt) VALUES ($version, $appliedAt);",
                cancellationToken,
                ("$version", migration.Version),
                ("$appliedAt", DateTimeOffset.UtcNow.ToString("O"))).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<Migration> GetMigrations()
    {
        var migrations = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Select(resourceName =>
            {
                var match = MigrationResourcePattern.Match(resourceName);
                return match.Success
                    ? new Migration(int.Parse(match.Groups["version"].Value), resourceName)
                    : null;
            })
            .OfType<Migration>()
            .OrderBy(migration => migration.Version)
            .ToArray();

        if (migrations.Length == 0)
        {
            throw new InvalidOperationException("No database migration resources were found.");
        }

        for (var index = 0; index < migrations.Length; index++)
        {
            var expectedVersion = index + 1;
            if (migrations[index].Version != expectedVersion)
            {
                throw new InvalidOperationException($"Database migrations must start at version 1 and be contiguous; expected version {expectedVersion}.");
            }
        }

        return migrations;
    }

    private static async Task<bool> IsAppliedAsync(SqliteConnection connection, SqliteTransaction transaction, int version, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM SchemaMigrations WHERE Version = $version);";
        command.Parameters.AddWithValue("$version", version);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
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

    private static async Task<string> ReadMigrationAsync(string resourceName, CancellationToken cancellationToken)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing database migration resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record Migration(int Version, string ResourceName);
}
