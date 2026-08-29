using Microsoft.Extensions.DependencyInjection;

namespace Ailo.AI.Conversations;

public static class AiloAiConversationExtensions
{
    public static IServiceCollection AddAiloAiConversations(this IServiceCollection services)
    {
        services.AddScoped<ConversationRepository>()
            .AddScoped<ConversationService>()
            .AddSingleton<SessionRunLock>();
        return services;
    }
}