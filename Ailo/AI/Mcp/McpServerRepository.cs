using Ailo.Data;
using Microsoft.Data.Sqlite;

namespace Ailo.AI.Mcp;

public sealed class McpServerRepository
{
    private readonly SqliteDatabase _database;

    public McpServerRepository(SqliteDatabase database) => _database = database;

    public async Task<IReadOnlyList<McpServer>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Transport, Endpoint, Command, ArgumentsJson, EnvironmentJson, HeadersJson, IsEnabled, CreatedAt, UpdatedAt FROM McpServers ORDER BY Name;";
        var result = new List<McpServer>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadServer(reader));
        }

        return result;
    }

    public async Task<McpServer?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Transport, Endpoint, Command, ArgumentsJson, EnvironmentJson, HeadersJson, IsEnabled, CreatedAt, UpdatedAt FROM McpServers WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadServer(reader) : null;
    }

    public async Task SaveAsync(McpServer server, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO McpServers (Id, Name, Transport, Endpoint, Command, ArgumentsJson, EnvironmentJson, HeadersJson, IsEnabled, CreatedAt, UpdatedAt)
            VALUES ($id, $name, $transport, $endpoint, $command, $arguments, $environment, $headers, $enabled, $createdAt, $updatedAt)
            ON CONFLICT(Id) DO UPDATE SET Name = excluded.Name, Transport = excluded.Transport,
                Endpoint = excluded.Endpoint, Command = excluded.Command, ArgumentsJson = excluded.ArgumentsJson,
                EnvironmentJson = excluded.EnvironmentJson, HeadersJson = excluded.HeadersJson,
                IsEnabled = excluded.IsEnabled, UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$id", server.Id);
        command.Parameters.AddWithValue("$name", server.Name);
        command.Parameters.AddWithValue("$transport", (int)server.Transport);
        command.Parameters.AddWithValue("$endpoint", server.Endpoint ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$command", server.Command ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$arguments", server.ArgumentsJson);
        command.Parameters.AddWithValue("$environment", server.EnvironmentJson);
        command.Parameters.AddWithValue("$headers", server.HeadersJson);
        command.Parameters.AddWithValue("$enabled", server.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", server.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", server.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM McpServers WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<McpTool>> GetToolsAsync(string serverId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ServerId, Name, Description, IsEnabled, UpdatedAt FROM McpTools WHERE ServerId = $serverId ORDER BY Name;";
        command.Parameters.AddWithValue("$serverId", serverId);
        var result = new List<McpTool>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadTool(reader));
        }

        return result;
    }

    public async Task ReplaceToolsAsync(string serverId, IReadOnlyList<(string Name, string? Description)> tools, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var current = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT Name, IsEnabled FROM McpTools WHERE ServerId = $serverId;";
            read.Parameters.AddWithValue("$serverId", serverId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                current[reader.GetString(0)] = reader.GetInt64(1) == 1;
            }
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM McpTools WHERE ServerId = $serverId;";
            delete.Parameters.AddWithValue("$serverId", serverId);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO McpTools (Id, ServerId, Name, Description, IsEnabled, UpdatedAt) VALUES ($id, $serverId, $name, $description, $enabled, $updatedAt);";
        var idParameter = insert.Parameters.Add("$id", SqliteType.Text);
        var serverParameter = insert.Parameters.Add("$serverId", SqliteType.Text);
        var nameParameter = insert.Parameters.Add("$name", SqliteType.Text);
        var descriptionParameter = insert.Parameters.Add("$description", SqliteType.Text);
        var enabledParameter = insert.Parameters.Add("$enabled", SqliteType.Integer);
        var updatedParameter = insert.Parameters.Add("$updatedAt", SqliteType.Text);
        serverParameter.Value = serverId;
        updatedParameter.Value = DateTimeOffset.UtcNow.ToString("O");
        foreach (var tool in tools.Where(tool => !string.IsNullOrWhiteSpace(tool.Name)).DistinctBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase))
        {
            idParameter.Value = Guid.NewGuid().ToString("N");
            nameParameter.Value = tool.Name.Trim();
            descriptionParameter.Value = tool.Description ?? (object)DBNull.Value;
            enabledParameter.Value = current.TryGetValue(tool.Name, out var enabled) && !enabled ? 0 : 1;
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetToolEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE McpTools SET IsEnabled = $enabled, UpdatedAt = $updatedAt WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static McpServer ReadServer(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), (McpTransportKind)reader.GetInt32(2),
        reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt64(8) == 1,
        DateTimeOffset.Parse(reader.GetString(9)), DateTimeOffset.Parse(reader.GetString(10)));

    private static McpTool ReadTool(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetInt64(4) == 1, DateTimeOffset.Parse(reader.GetString(5)));
}
