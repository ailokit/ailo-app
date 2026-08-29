using Ailo.AI.Mcp;
using Ailo.Data;

namespace Ailo.Tests;

public sealed class McpServerRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task SaveRefreshAndDeleteServerAndTools()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var repository = new McpServerRepository(database);
        var now = DateTimeOffset.UtcNow;
        var server = new McpServer("server", "Demo", McpTransportKind.Stdio, null, "node", "[\"server.js\"]", "{}", "{}", true, now, now);

        await repository.SaveAsync(server);
        await repository.ReplaceToolsAsync(server.Id, [("one", "First"), ("two", "Second")]);
        var tools = await repository.GetToolsAsync(server.Id);
        await repository.SetToolEnabledAsync(tools[0].Id, false);
        await repository.ReplaceToolsAsync(server.Id, [("one", "First"), ("three", "Third")]);

        var refreshed = await repository.GetToolsAsync(server.Id);
        Assert.False(Assert.Single(refreshed, tool => tool.Name == "one").IsEnabled);
        Assert.True(Assert.Single(refreshed, tool => tool.Name == "three").IsEnabled);

        await repository.DeleteAsync(server.Id);
        Assert.Empty(await repository.GetAllAsync());
        Assert.Empty(await repository.GetToolsAsync(server.Id));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
