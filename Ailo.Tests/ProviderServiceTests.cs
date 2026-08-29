using Ailo.AI.Providers;
using Ailo.Data;
using Ailo.Models;
using Ailo.Services;

namespace Ailo.Tests;

public sealed class ProviderServiceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public void Validate_RequiresEndpointForOllamaAndCompatibleProviders()
    {
        var provider = CreateProvider(ProviderType.Ollama) with { ApiKey = string.Empty, Endpoint = null };

        Assert.Throws<ArgumentException>(() => ProviderService.Validate(provider));
    }

    [Fact]
    public async Task SaveAsync_StoresValidatedProvider()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        var service = new ProviderService(new ApiProviderRepository(database));
        var provider = CreateProvider(ProviderType.OpenAi);

        await service.SaveAsync(provider);

        Assert.Equal(provider.Id, Assert.Single(await service.GetAllAsync()).Id);
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    private static ApiProvider CreateProvider(ProviderType type) => new("provider", "Provider", type, "key", "https://example.test", "model", true, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
