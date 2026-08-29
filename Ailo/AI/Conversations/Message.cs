namespace Ailo.AI.Conversations;

public sealed record Message(
    string Id,
    string ConversationId,
    long SequenceNo,
    MessageRole Role,
    string Content,
    MessageStatus Status,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Attached image metadata, persisted as JSON in the Messages.Attachments column.</summary>
    public IReadOnlyList<MessageAttachment> Attachments { get; init; } = [];

}
