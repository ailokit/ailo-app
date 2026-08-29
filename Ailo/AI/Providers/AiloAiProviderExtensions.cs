using Microsoft.Extensions.DependencyInjection;

namespace Ailo.AI.Providers;

public static class AiloAiProviderExtensions
{
    public static IServiceCollection AddAiloAiProviders(this IServiceCollection services)
    {
        services.AddScoped<ApiProviderRepository>();
        services.AddScoped<IProviderConnectionTester, ProviderConnectionTester>();
        services.AddScoped<ProviderService>();
        
        return services;
    }
}