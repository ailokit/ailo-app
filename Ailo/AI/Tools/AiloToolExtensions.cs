using Microsoft.Extensions.DependencyInjection;

namespace Ailo.AI.Tools;

public static class AiloToolExtensions
{
    public static IServiceCollection AddAiloTools(this IServiceCollection services)
    {
        services.AddScoped<ChatWorkspace>();
        services.AddScoped<ChatToolRegistry>();
        AddInternalTools(services);
        return services;
    }

    private static void AddInternalTools(IServiceCollection services)
    {
        services.AddScoped<WebContentTool>();
        services.AddScoped<OpenWebpageTool>();
        services.AddScoped<WorkspaceFileSystemTool>();
        services.AddScoped<ScheduleNotificationTool>();
        services.AddScoped<SystemNotificationTool>();
        services.AddScoped<SystemInformationTool>();
        services.AddScoped<ScheduleAgentJobTool>();
        services.AddScoped<ManageScheduledJobsTool>();
        services.AddScoped<IChatToolProvider, DefaultToolProvider>();
        services.AddScoped<IChatToolProvider, WorkspaceToolProvider>();
    }
}
