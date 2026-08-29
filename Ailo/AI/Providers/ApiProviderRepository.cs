using Ailo.Data;
using Microsoft.Data.Sqlite;

namespace Ailo.AI.Providers;

public sealed class ApiProviderRepository
{
    private readonly SqliteDatabase _database;

    public ApiProviderRepository(SqliteDatabase database) => _database = database;

    public async Task SaveAsync(ApiProvider provider, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (provider.IsDefault)
        {
            await ExecuteAsync(connection, transaction, "UPDATE ApiProviders SET IsDefault = 0 WHERE Id <> $id;", cancellationToken, ("$id", provider.Id)).ConfigureAwait(false);
        }

        await ExecuteAsync(connection, transaction, """
            INSERT INTO ApiProviders (Id, Name, ProviderType, ApiKey, Endpoint, ModelId, IsDefault, IsEnabled, CreatedAt, UpdatedAt)
            VALUES ($id, $name, $providerType, $apiKey, $endpoint, $modelId, $isDefault, $isEnabled, $createdAt, $updatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name, ProviderType = excluded.ProviderType, ApiKey = excluded.ApiKey,
                Endpoint = excluded.Endpoint, ModelId = excluded.ModelId, IsDefault = excluded.IsDefault,
                IsEnabled = excluded.IsEnabled, UpdatedAt = excluded.UpdatedAt;
            """, cancellationToken,
            ("$id", provider.Id), ("$name", provider.Name), ("$providerType", (int)provider.ProviderType),
            ("$apiKey", provider.ApiKey), ("$endpoint", provider.Endpoint ?? (object)DBNull.Value), ("$modelId", provider.ModelId),
            ("$isDefault", provider.IsDefault ? 1 : 0), ("$isEnabled", provider.IsEnabled ? 1 : 0),
            ("$createdAt", provider.CreatedAt.ToString("O")), ("$updatedAt", provider.UpdatedAt.ToString("O"))).ConfigureAwait(false);

        await ExecuteAsync(connection, transaction, "DELETE FROM ProviderModels WHERE ProviderId = $providerId;", cancellationToken, ("$providerId", provider.Id)).ConfigureAwait(false);
        var models = provider.ModelIds.Append(provider.ModelId).Where(model => !string.IsNullOrWhiteSpace(model)).Select(model => model.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            await ExecuteAsync(connection, transaction, "INSERT INTO ProviderModels (ProviderId, ModelId, IsMultimodal) VALUES ($providerId, $modelId, $isMultimodal);", cancellationToken, ("$providerId", provider.Id), ("$modelId", model), ("$isMultimodal", provider.MultimodalModelIds.Contains(model) ? 1 : 0)).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ApiProvider>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, ProviderType, ApiKey, Endpoint, ModelId, IsDefault, IsEnabled, CreatedAt, UpdatedAt FROM ApiProviders ORDER BY IsDefault DESC, Name;";
        var providers = new List<ApiProvider>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                providers.Add(ReadProvider(reader));
            }
        }

        return await AddModelsAsync(connection, providers, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApiProvider?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, ProviderType, ApiKey, Endpoint, ModelId, IsDefault, IsEnabled, CreatedAt, UpdatedAt FROM ApiProviders WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var provider = ReadProvider(reader);
        await reader.DisposeAsync().ConfigureAwait(false);
        var providers = await AddModelsAsync(connection, [provider], cancellationToken).ConfigureAwait(false);
        return providers[0];
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ApiProviders SET IsEnabled = 0, IsDefault = 0 WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ApiProvider ReadProvider(SqliteDataReader reader) => new(reader.GetString(0), reader.GetString(1), (ProviderType)reader.GetInt32(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetInt64(6) == 1, reader.GetInt64(7) == 1, DateTimeOffset.Parse(reader.GetString(8)), DateTimeOffset.Parse(reader.GetString(9)));

    private static async Task<IReadOnlyList<ApiProvider>> AddModelsAsync(SqliteConnection connection, IReadOnlyList<ApiProvider> providers, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var result = new List<ApiProvider>(providers.Count);
        foreach (var provider in providers)
        {
            command.Parameters.Clear();
            command.CommandText = "SELECT ModelId, IsMultimodal FROM ProviderModels WHERE ProviderId = $providerId ORDER BY ModelId;";
            command.Parameters.AddWithValue("$providerId", provider.Id);
            await using var modelsReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var models = new List<string>();
            var multimodal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await modelsReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var model = modelsReader.GetString(0);
                models.Add(model);
                if (modelsReader.GetInt64(1) == 1) multimodal.Add(model);
            }
            result.Add(provider with
            {
                ModelIds = models.Count == 0 ? [provider.ModelId] : models,
                MultimodalModelIds = multimodal
            });
        }

        return result;
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
}
