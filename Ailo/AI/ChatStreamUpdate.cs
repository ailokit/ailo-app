namespace Ailo.AI;

public enum ChatStreamUpdateKind
{
    Text,
    Reasoning,
    ToolStarted,
    ToolCompleted
}

/// <summary>A streamed UI update in the same order in which it was generated.</summary>
public sealed record ChatStreamUpdate(ChatStreamUpdateKind Kind, string? Content = null, string? ToolCallId = null)
{
    public static ChatStreamUpdate Text(string content) => new(ChatStreamUpdateKind.Text, content);
    public static ChatStreamUpdate Reasoning(string content) => new(ChatStreamUpdateKind.Reasoning, content);
    public static ChatStreamUpdate ToolStarted(string callId, string notice) =>
        new(ChatStreamUpdateKind.ToolStarted, notice, callId);
    public static ChatStreamUpdate ToolCompleted(string callId) =>
        new(ChatStreamUpdateKind.ToolCompleted, ToolCallId: callId);
}
