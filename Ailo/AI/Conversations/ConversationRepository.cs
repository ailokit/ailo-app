using Ailo.Data;

namespace Ailo.AI.Conversations;

/// <summary>Provides durable storage for conversation metadata and serialized agent-session state.</summary>
public sealed class ConversationRepository
{
    private readonly SqliteDatabase _database;

    public ConversationRepository(SqliteDatabase database) => _database = database;

    /// <summary>Inserts a conversation or updates its mutable session and archive state.</summary>
    public async Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Conversations (Id, Title, ProviderId, SkillId, ProviderConfiguration, SkillVersion, AgentType, AgentConfigurationHash, MafVersion, SessionState, SessionStatus, IsArchived, CreatedAt, UpdatedAt)
            VALUES ($id, $title, $providerId, $skillId, $providerConfiguration, $skillVersion, $agentType, $hash, $mafVersion, $sessionState, $sessionStatus, $isArchived, $createdAt, $updatedAt)
            ON CONFLICT(Id) DO UPDATE SET Title = excluded.Title, SessionState = excluded.SessionState, SessionStatus = excluded.SessionStatus, IsArchived = excluded.IsArchived, UpdatedAt = excluded.UpdatedAt;
            """;
        Add(command, "$id", conversation.Id); Add(command, "$title", conversation.Title); Add(command, "$providerId", conversation.ProviderId); Add(command, "$skillId", conversation.SkillId ?? (object)DBNull.Value); Add(command, "$providerConfiguration", conversation.ProviderConfiguration); Add(command, "$skillVersion", conversation.SkillVersion ?? (object)DBNull.Value); Add(command, "$agentType", conversation.AgentType); Add(command, "$hash", conversation.AgentConfigurationHash); Add(command, "$mafVersion", conversation.MafVersion); Add(command, "$sessionState", conversation.SessionState); Add(command, "$sessionStatus", (int)conversation.SessionStatus); Add(command, "$isArchived", conversation.IsArchived ? 1 : 0); Add(command, "$createdAt", conversation.CreatedAt.ToString("O")); Add(command, "$updatedAt", conversation.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns a conversation by identifier, including archived conversations.</summary>
    public async Task<Conversation?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, ProviderId, SkillId, ProviderConfiguration, SkillVersion, AgentType, AgentConfigurationHash, MafVersion, SessionState, SessionStatus, IsArchived, CreatedAt, UpdatedAt FROM Conversations WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new Conversation(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), (SessionStatus)reader.GetInt32(10), reader.GetInt64(11) == 1, DateTimeOffset.Parse(reader.GetString(12)), DateTimeOffset.Parse(reader.GetString(13)));
    }

    /// <summary>Returns the most recently updated active conversations for the chat selector.</summary>
    public async Task<IReadOnlyList<Conversation>> GetRecentAsync(CancellationToken cancellationToken = default)
        => await GetPageAsync(0, 15, cancellationToken).ConfigureAwait(false);

    /// <summary>Returns one zero-based page of active conversations ordered by most recent update.</summary>
    public async Task<IReadOnlyList<Conversation>> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, ProviderId, SkillId, ProviderConfiguration, SkillVersion, AgentType, AgentConfigurationHash, MafVersion, SessionState, SessionStatus, IsArchived, CreatedAt, UpdatedAt FROM Conversations WHERE IsArchived = 0 ORDER BY UpdatedAt DESC LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", page * pageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var conversations = new List<Conversation>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            conversations.Add(new Conversation(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), (SessionStatus)reader.GetInt32(10), reader.GetInt64(11) == 1, DateTimeOffset.Parse(reader.GetString(12)), DateTimeOffset.Parse(reader.GetString(13))));
        }

        return conversations;
    }

    /// <summary>
    /// Soft-deletes a conversation so it disappears from normal lists without destroying its persisted data.
    /// </summary>
    public async Task ArchiveAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Conversations SET IsArchived = 1, UpdatedAt = $updatedAt WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Add(Microsoft.Data.Sqlite.SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);
}
