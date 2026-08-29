using Microsoft.Extensions.DependencyInjection;

namespace Ailo.AI.Skills;

public static class AiloAiSkillsExtensions
{
    public static IServiceCollection AddAiloSkills(this IServiceCollection services)
    {
        services.AddScoped<SkillRepository>()
            .AddScoped<SkillService>();

        return services;
    }
}