using Ailo.Data;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Ailo.Tests;

public sealed class DatabaseMigratorTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task MigrateAsync_CreatesSchemaAndSeedsBuiltInSkills_Idempotently()
    {
        var database = new SqliteDatabase(_databasePath);
        var migrator = new DatabaseMigrator(database);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        await using var connection = await database.OpenConnectionAsync();
        Assert.Equal(12L, await ScalarAsync(connection, "SELECT COUNT(*) FROM SchemaMigrations;"));
        Assert.Equal(5L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Skills WHERE IsBuiltIn = 1;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Messages';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ProviderModels';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('ProviderModels') WHERE name = 'IsMultimodal';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('Messages') WHERE name = 'Attachments';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info('Messages') WHERE name = 'Reasoning';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'McpServers';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'McpTools';"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ScheduledNotifications';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'CronJobs';"));
    }

    [Fact]
    public async Task MigrateAsync_RefreshesApplicationOwnedLegacyBrandValues()
    {
        var database = new SqliteDatabase(_databasePath);
        var migrator = new DatabaseMigrator(database);
        await migrator.MigrateAsync();

        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Skills SET SystemPrompt = 'You are Chater, a helpful AI assistant.' WHERE Id = 'builtin-chat'; INSERT INTO ApiProviders (Id, Name, ProviderType, ApiKey, ModelId, IsDefault, IsEnabled, CreatedAt, UpdatedAt) VALUES ('legacy-provider', 'Legacy', 0, '', 'model', 1, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP); INSERT INTO Conversations (Id, Title, ProviderId, ProviderConfiguration, AgentType, AgentConfigurationHash, MafVersion, SessionState, SessionStatus, CreatedAt, UpdatedAt) VALUES ('legacy-conversation', 'Legacy', 'legacy-provider', '{}', 'legacy', 'hash', 'version', '{}', 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP); INSERT INTO Messages (Id, ConversationId, SequenceNo, Role, Content, Status, CreatedAt, UpdatedAt) VALUES ('legacy-message', 'legacy-conversation', 1, 1, '<!-- chater-tool -->legacy<!-- /chater-tool -->', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP); DELETE FROM SchemaMigrations WHERE Version IN (11, 12);";
            await command.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();

        await using var upgraded = await database.OpenConnectionAsync();
        Assert.Equal(12L, await ScalarAsync(upgraded, "SELECT COUNT(*) FROM SchemaMigrations;"));
        Assert.Equal("You are Ailo, a helpful AI assistant.", await ScalarAsyncTextAsync(upgraded, "SELECT SystemPrompt FROM Skills WHERE Id = 'builtin-chat';"));
        Assert.Equal("<!-- ailo-tool -->legacy<!-- /ailo-tool -->", await ScalarAsyncTextAsync(upgraded, "SELECT Content FROM Messages WHERE Id = 'legacy-message';"));
    }

    [Fact]
    public async Task MigrateAsync_ImportsLegacyNotificationsAsCronJobs()
    {
        var database = new SqliteDatabase(_databasePath);
        var migrator = new DatabaseMigrator(database);
        await migrator.MigrateAsync();

        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                DROP TABLE CronJobs;
                DELETE FROM SchemaMigrations WHERE Version = 9;
                DELETE FROM SchemaMigrations WHERE Version = 10;
                CREATE TABLE ScheduledNotifications (
                    Id TEXT PRIMARY KEY,
                    CronExpression TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Subtitle TEXT NULL,
                    Body TEXT NOT NULL,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    LastRunAtUtc TEXT NULL,
                    NextRunAtUtc TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                INSERT INTO ScheduledNotifications
                    (Id, CronExpression, Title, Subtitle, Body, IsEnabled, LastRunAtUtc, NextRunAtUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ('legacy-notification', '0 9 * * 1-5', 'Title', 'Subtitle', 'Body', 1, NULL,
                     '2026-09-01T01:00:00.0000000+00:00', '2026-08-01T01:00:00.0000000+00:00', '2026-08-01T01:00:00.0000000+00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();

        await using var upgraded = await database.OpenConnectionAsync();
        await using var query = upgraded.CreateCommand();
        query.CommandText = "SELECT Id, JobType, ParametersJson FROM CronJobs ORDER BY Id LIMIT 1;";
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetInt32(0) > 0);
        Assert.Equal("native_notification", reader.GetString(1));
        using var parameters = JsonDocument.Parse(reader.GetString(2));
        Assert.Equal("Title", parameters.RootElement.GetProperty("Title").GetString());
        Assert.Equal("Body", parameters.RootElement.GetProperty("Body").GetString());
        Assert.Equal("Subtitle", parameters.RootElement.GetProperty("Subtitle").GetString());
        Assert.Equal(0L, await ScalarAsync(upgraded, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ScheduledNotifications';"));
    }

    [Fact]
    public async Task MigrateAsync_ConvertsExistingGuidCronJobIdsToAutoIncrementIds()
    {
        var database = new SqliteDatabase(_databasePath);
        var migrator = new DatabaseMigrator(database);
        await migrator.MigrateAsync();

        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                DROP TABLE CronJobs;
                DELETE FROM SchemaMigrations WHERE Version = 10;
                CREATE TABLE CronJobs (
                    Id TEXT PRIMARY KEY,
                    JobType TEXT NOT NULL,
                    CronExpression TEXT NOT NULL,
                    ParametersJson TEXT NOT NULL,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    LastRunAtUtc TEXT NULL,
                    NextRunAtUtc TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                INSERT INTO CronJobs
                    (Id, JobType, CronExpression, ParametersJson, IsEnabled, NextRunAtUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ('legacy-guid', 'native_notification', '0 9 * * *', '{}', 1,
                     '2026-09-01T01:00:00.0000000+00:00', '2026-08-01T01:00:00.0000000+00:00', '2026-08-01T01:00:00.0000000+00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();

        await using var upgraded = await database.OpenConnectionAsync();
        await using var typeQuery = upgraded.CreateCommand();
        typeQuery.CommandText = "SELECT type FROM pragma_table_info('CronJobs') WHERE name = 'Id';";
        Assert.Equal("INTEGER", await typeQuery.ExecuteScalarAsync());
        Assert.Equal(1L, await ScalarAsync(upgraded, "SELECT COUNT(*) FROM CronJobs;"));
        Assert.True(Convert.ToInt32(await ScalarAsync(upgraded, "SELECT Id FROM CronJobs;")) > 0);
    }

    [Fact]
    public async Task OpenConnectionAsync_EnforcesForeignKeys()
    {
        var database = new SqliteDatabase(_databasePath);
        var migrator = new DatabaseMigrator(database);
        await migrator.MigrateAsync();
        await using var connection = await database.OpenConnectionAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Messages (Id, ConversationId, SequenceNo, Role, Content, Status, CreatedAt, UpdatedAt) VALUES ('message', 'missing', 1, 0, 'x', 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);";

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task MigrateAsync_UpgradesAnExistingV3DatabaseWithMessageReasoning()
    {
        var database = new SqliteDatabase(_databasePath);
        var migrator = new DatabaseMigrator(database);
        await migrator.MigrateAsync();

        // Simulate a database created by the previous application version.
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "ALTER TABLE Messages DROP COLUMN Reasoning; DELETE FROM SchemaMigrations WHERE Version = 4;";
            await command.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();

        await using var upgraded = await database.OpenConnectionAsync();
        Assert.Equal(1L, await ScalarAsync(upgraded, "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = 4;"));
        Assert.Equal(1L, await ScalarAsync(upgraded, "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = 5;"));
        Assert.Equal(1L, await ScalarAsync(upgraded, "SELECT COUNT(*) FROM pragma_table_info('Messages') WHERE name = 'Reasoning';"));
    }

    [Fact]
    public async Task MigrateAsync_RepairsAnExistingV4DatabaseMissingMessageReasoning()
    {
        var database = new SqliteDatabase(_databasePath);
        var migrator = new DatabaseMigrator(database);
        await migrator.MigrateAsync();

        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "ALTER TABLE Messages DROP COLUMN Reasoning; DELETE FROM SchemaMigrations WHERE Version = 5;";
            await command.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();

        await using var repaired = await database.OpenConnectionAsync();
        Assert.Equal(1L, await ScalarAsync(repaired, "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = 5;"));
        Assert.Equal(1L, await ScalarAsync(repaired, "SELECT COUNT(*) FROM pragma_table_info('Messages') WHERE name = 'Reasoning';"));
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

    private static async Task<string> ScalarAsyncTextAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }
}
