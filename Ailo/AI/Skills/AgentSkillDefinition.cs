namespace Ailo.AI.Skills;

/// <summary>A file-based Agent Skill discovered from one of the configured local sources.</summary>
public sealed record AgentSkillDefinition(
    string Id,
    string Source,
    string SourceRoot,
    string DirectoryPath,
    string Name,
    string Description,
    bool HasScripts,
    bool IsEnabled,
    DateTimeOffset LastSeenAt,
    DateTimeOffset UpdatedAt);

/// <summary>One local directory that contributes Agent Skills.</summary>
public sealed record AgentSkillSourceDirectory(string Name, string Path);
