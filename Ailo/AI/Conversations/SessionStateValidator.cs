namespace Ailo.AI.Conversations;

public static class SessionStateValidator
{
    public static bool CanRestore(SessionSnapshot persisted, SessionSnapshot requested) =>
        persisted.AgentType == requested.AgentType &&
        persisted.ProviderId == requested.ProviderId &&
        persisted.ModelId == requested.ModelId &&
        persisted.SkillVersion == requested.SkillVersion &&
        persisted.MafVersion == requested.MafVersion &&
        persisted.ConfigurationHash == requested.ConfigurationHash &&
        !string.IsNullOrWhiteSpace(persisted.SerializedState);
}
