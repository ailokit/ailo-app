using Ailo.AI.Conversations;
using Ailo.Data;
using Ailo.Services;

namespace Ailo.Tests;

public sealed class StartupRecoveryServiceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task RecoverAsync_CancelsOnlyIncompleteMessages()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        await SeedConversationAsync(database);
        await SeedMessageAsync(database, "pending", 1, MessageStatus.Pending);
        await SeedMessageAsync(database, "streaming", 2, MessageStatus.Streaming);
        await SeedMessageAsync(database, "completed", 3, MessageStatus.Completed);
        var recovery = new StartupRecoveryService(new MessageRepository(database));

        var changed = await recovery.RecoverAsync();

        Assert.Equal(2, changed);
        await using var connection = await database.OpenConnectionAsync();
        Assert.Equal((int)MessageStatus.Cancelled, await GetStatusAsync(connection, "pending"));
        Assert.Equal((int)MessageStatus.Cancelled, await GetStatusAsync(connection, "streaming"));
        Assert.Equal((int)MessageStatus.Completed, await GetStatusAsync(connection, "completed"));
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private static async Task SeedConversationAsync(SqliteDatabase database)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ApiProviders (Id, Name, ProviderType, ApiKey, ModelId, CreatedAt, UpdatedAt)
            VALUES ('provider', 'Test', 0, 'key', 'model', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            INSERT INTO Conversations (Id, Title, ProviderId, ProviderConfiguration, AgentType, AgentConfigurationHash, MafVersion, SessionState, SessionStatus, CreatedAt, UpdatedAt)
            VALUES ('conversation', 'Test', 'provider', '{}', 'test', 'hash', 'test', '{}', 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedMessageAsync(SqliteDatabase database, string id, int sequenceNo, MessageStatus status)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Messages (Id, ConversationId, SequenceNo, Role, Content, Status, CreatedAt, UpdatedAt)
            VALUES ($id, 'conversation', $sequenceNo, 0, 'content', $status, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$sequenceNo", sequenceNo);
        command.Parameters.AddWithValue("$status", (int)status);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> GetStatusAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM Messages WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
