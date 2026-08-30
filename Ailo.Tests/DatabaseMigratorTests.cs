using Ailo.Data;
using Microsoft.Data.Sqlite;

namespace Ailo.Tests;

public sealed class DatabaseMigratorTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task MigrateAsync_CreatesTheCurrentSchemaAndBuiltInSkills_Idempotently()
    {
        var database = new SqliteDatabase(_databasePath);
        var migrator = new DatabaseMigrator(database);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        await using var connection = await database.OpenConnectionAsync();
        Assert.Equal(5L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Skills WHERE IsBuiltIn = 1;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Messages';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ProviderModels';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ProviderModels') WHERE name = 'IsMultimodal';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('Messages') WHERE name = 'Attachments';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('Messages') WHERE name = 'Reasoning';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'McpServers';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'McpTools';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'CronJobs';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('CronJobs') WHERE name = 'IsOneTime';"));
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT COUNT(*) FROM SchemaMigrations;"));
    }

    [Fact]
    public async Task OpenConnectionAsync_EnforcesForeignKeys()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        await using var connection = await database.OpenConnectionAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Messages (Id, ConversationId, SequenceNo, Role, Content, Status, CreatedAt, UpdatedAt) VALUES ('message', 'missing', 1, 0, 'x', 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);";

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task MigrateAsync_RepairsCronJobsColumn_WhenMigrationWasAlreadyRecorded()
    {
        var database = new SqliteDatabase(_databasePath);
        await using (var connection = await database.OpenConnectionAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE SchemaMigrations (Version INTEGER NOT NULL PRIMARY KEY, AppliedAt TEXT NOT NULL);
                INSERT INTO SchemaMigrations (Version, AppliedAt) VALUES (1, CURRENT_TIMESTAMP), (2, CURRENT_TIMESTAMP);
                CREATE TABLE CronJobs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    JobType TEXT NOT NULL,
                    CronExpression TEXT NOT NULL,
                    ParametersJson TEXT NOT NULL,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    LastRunAtUtc TEXT NULL,
                    NextRunAtUtc TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        await new DatabaseMigrator(database).MigrateAsync();

        await using var migratedConnection = await database.OpenConnectionAsync();
        Assert.Equal(1L, await ScalarAsync(migratedConnection, "SELECT COUNT(*) FROM pragma_table_info('CronJobs') WHERE name = 'IsOneTime';"));
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
