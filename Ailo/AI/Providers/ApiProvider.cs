namespace Ailo.AI.Providers;

public sealed record ApiProvider(
    string Id,
    string Name,
    ProviderType ProviderType,
    string ApiKey,
    string? Endpoint,
    string ModelId,
    bool IsDefault,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    // ModelId remains the active model for backwards compatibility. ModelIds
    // contains all models that share this provider/API key.
    public IReadOnlyList<string> ModelIds { get; init; } = [ModelId];
    /// <summary>Model IDs (subset of <see cref="ModelIds"/>) that accept image input.</summary>
    public IReadOnlySet<string> MultimodalModelIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public string ModelSummary => string.Join(", ", ModelIds);
}
