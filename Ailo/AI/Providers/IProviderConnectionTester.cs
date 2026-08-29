namespace Ailo.AI.Providers;

public interface IProviderConnectionTester
{
    Task<ProviderConnectionResult> TestAsync(ApiProvider provider, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> FetchModelsAsync(ApiProvider provider, CancellationToken cancellationToken = default);
}

public sealed record ProviderConnectionResult(bool IsSuccess, string Code, string Message)
{
    public static ProviderConnectionResult Success() => new(true, "ok", "Connection succeeded.");
}
