using Ailo.Composition;
using Ailo.AI.Tools;
using Ailo.Data;
using Ailo.Jobs;
using Ailo.Services;
using Ailo.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Ailo.Tests;

public sealed class CompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Ailo.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void InitializeAiloDatabase_RegistersAndInitializesDataServices()
    {
        using var services = new ServiceCollection().AddAiloApplication(new AppPaths(_root)).BuildServiceProvider();

        services.InitializeAiloDatabase();

        Assert.NotNull(services.GetRequiredService<SqliteDatabase>());
        Assert.NotNull(services.GetRequiredService<StartupRecoveryService>());
        Assert.NotNull(services.GetRequiredService<CronJobRepository>());
        Assert.NotNull(services.GetRequiredService<CronJobScheduler>());
        Assert.True(File.Exists(Path.Combine(_root, "ailo.db")));
    }

    [Fact]
    public void Application_ResolvesShellConfiguration()
    {
        using var services = new ServiceCollection().AddAiloApplication(new AppPaths(_root)).BuildServiceProvider();

        var state = services.GetRequiredService<AppState>();

        Assert.NotNull(state.ShellToolConfiguration);
    }

    [Fact]
    public async Task ChatWorkspaceAndFileTools_AreIsolatedPerWindowScope()
    {
        using var services = new ServiceCollection().AddAiloApplication(new AppPaths(_root)).BuildServiceProvider();
        using var firstScope = services.CreateScope();
        using var secondScope = services.CreateScope();

        var firstWorkspace = firstScope.ServiceProvider.GetRequiredService<ChatWorkspace>();
        var sameWorkspace = firstScope.ServiceProvider.GetRequiredService<ChatWorkspace>();
        var secondWorkspace = secondScope.ServiceProvider.GetRequiredService<ChatWorkspace>();
        var toolNames = (await firstScope.ServiceProvider.GetRequiredService<ChatToolRegistry>().GetTools())
            .Select(tool => tool.Name)
            .ToArray();

        Assert.Same(firstWorkspace, sameWorkspace);
        Assert.NotSame(firstWorkspace, secondWorkspace);
        Assert.Contains("get_workspace_entries", toolNames);
        Assert.Contains("read_workspace_file", toolNames);
        Assert.Contains("write_workspace_file", toolNames);
        Assert.Contains("create_workspace_directory", toolNames);
        Assert.Contains("list_workspace_directory", toolNames);
        Assert.Contains("schedule_notification", toolNames);
        Assert.Contains("show_notification", toolNames);
        Assert.Contains("get_system_information", toolNames);
        Assert.Contains("open_webpage_in_browser", toolNames);
        Assert.Contains("schedule_agent_job", toolNames);
        Assert.Contains("list_scheduled_jobs", toolNames);
        Assert.Contains("update_scheduled_job", toolNames);
        Assert.Contains("delete_scheduled_job", toolNames);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
