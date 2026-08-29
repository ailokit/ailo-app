using System.Text.Json;
using Ailo.AI;
using Ailo.AI.Conversations;
using Ailo.Models;

namespace Ailo.Data;

/// <summary>Persists ordered user and assistant messages for each conversation.</summary>
public sealed class MessageRepository
{
    private readonly SqliteDatabase _database;

    public MessageRepository(SqliteDatabase database) => _database = database;

    /// <summary>
    /// Marks unfinished responses as cancelled after an unclean shutdown, preventing them from appearing active on restart.
    /// </summary>
    public async Task<int> CancelIncompleteMessagesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Messages
            SET Status = $cancelled,
                ErrorCode = 'app_interrupted',
                ErrorMessage = 'The application was closed before the response completed.',
                UpdatedAt = $updatedAt
            WHERE Status IN ($pending, $streaming);
            """;
        command.Parameters.AddWithValue("$cancelled", (int)MessageStatus.Cancelled);
        command.Parameters.AddWithValue("$pending", (int)MessageStatus.Pending);
        command.Parameters.AddWithValue("$streaming", (int)MessageStatus.Streaming);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Appends a message at its already allocated sequence number.</summary>
    public async Task AppendAsync(Message message, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Messages (Id, ConversationId, SequenceNo, Role, Content, Status, ErrorCode, ErrorMessage, Attachments, CreatedAt, UpdatedAt) VALUES ($id, $conversationId, $sequenceNo, $role, $content, $status, $errorCode, $errorMessage, $attachments, $createdAt, $updatedAt);";
        command.Parameters.AddWithValue("$id", message.Id); command.Parameters.AddWithValue("$conversationId", message.ConversationId); command.Parameters.AddWithValue("$sequenceNo", message.SequenceNo); command.Parameters.AddWithValue("$role", (int)message.Role); command.Parameters.AddWithValue("$content", message.Content); command.Parameters.AddWithValue("$status", (int)message.Status); command.Parameters.AddWithValue("$errorCode", message.ErrorCode ?? (object)DBNull.Value); command.Parameters.AddWithValue("$errorMessage", message.ErrorMessage ?? (object)DBNull.Value); command.Parameters.AddWithValue("$attachments", message.Attachments.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(message.Attachments.ToArray(), AiloJsonSerializerContext.Default.MessageAttachmentArray)); command.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("O")); command.Parameters.AddWithValue("$updatedAt", message.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Allocates the next message sequence number within a conversation.</summary>
    public async Task<long> GetNextSequenceNoAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(SequenceNo), 0) + 1 FROM Messages WHERE ConversationId = $conversationId;";
        command.Parameters.AddWithValue("$conversationId", conversationId);
        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 1L);
    }

    /// <summary>Returns messages in display and replay order.</summary>
    public async Task<IReadOnlyList<Message>> GetByConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ConversationId, SequenceNo, Role, Content, Status, ErrorCode, ErrorMessage, CreatedAt, UpdatedAt, Attachments FROM Messages WHERE ConversationId = $conversationId ORDER BY SequenceNo;";
        command.Parameters.AddWithValue("$conversationId", conversationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var messages = new List<Message>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var message = new Message(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), (MessageRole)reader.GetInt32(3), reader.GetString(4), (MessageStatus)reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), DateTimeOffset.Parse(reader.GetString(8)), DateTimeOffset.Parse(reader.GetString(9)));
            if (!reader.IsDBNull(10))
            {
                message = message with
                {
                    Attachments = JsonSerializer.Deserialize(reader.GetString(10), AiloJsonSerializerContext.Default.MessageAttachmentArray) ?? []
                };
            }

            messages.Add(message);
        }

        return messages;
    }

    /// <summary>Updates streamed content and its terminal or in-progress status.</summary>
    public async Task UpdateContentAndStatusAsync(
        string messageId,
        string content,
        MessageStatus status,
        string? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Messages SET Content = $content, Status = $status, ErrorCode = $errorCode, ErrorMessage = $errorMessage, UpdatedAt = $updatedAt WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", messageId);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$status", (int)status);
        command.Parameters.AddWithValue("$errorCode", errorCode ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", errorMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException($"Message '{messageId}' does not exist.");
        }
    }
}
