using Ailo.AI.Conversations;
using Ailo.AI.Providers;
using Ailo.AI.Mcp;
using Ailo.AI.Skills;
using Ailo.AI.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Ailo.AI;

public static class AiloAiExtensions
{
    public static IServiceCollection AddAiloAi(this IServiceCollection services)
    {
        services.AddScoped<ChatService>()
            .AddSingleton<McpServerRepository>()
            .AddSingleton<McpClientService>()
            .AddAiloTools()
            .AddAiloAiConversations()
            .AddAiloAiProviders()
            .AddAiloSkills();
        
        return services;
    }
}
