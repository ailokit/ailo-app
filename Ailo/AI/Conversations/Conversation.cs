namespace Ailo.AI.Conversations;

public sealed record Conversation(
    string Id,
    string Title,
    string ProviderId,
    string? SkillId,
    string ProviderConfiguration,
    int? SkillVersion,
    string AgentType,
    string AgentConfigurationHash,
    string MafVersion,
    string SessionState,
    SessionStatus SessionStatus,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string DisplayUpdatedAt => UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
