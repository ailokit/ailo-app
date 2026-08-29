namespace Ailo.AI.Skills;

public sealed record Skill(
    string Id,
    string Name,
    string? Description,
    string SystemPrompt,
    string? Icon,
    bool IsBuiltIn,
    bool IsEnabled,
    int SortOrder,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool CanDelete => IsEnabled;
}
