namespace Ailo.AI.Conversations;

public sealed record SessionSnapshot(string AgentType, string ProviderId, string ModelId, int? SkillVersion, string MafVersion, string ConfigurationHash, string SerializedState);
