using Ailo.AI;
using Ailo.AI.Skills;
using Ailo.Data;
using Ailo.Models;
using Ailo.Services;

namespace Ailo.Tests;

public sealed class SkillServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task SaveCustomAsync_CreatesAndVersionsCustomSkill()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var service = new SkillService(new SkillRepository(database));
        var now = DateTimeOffset.UtcNow;
        var draft = new Skill("custom", "Research", null, "Be rigorous.", null, false, true, 10, 0, now, now);

        var created = await service.SaveCustomAsync(draft);
        var updated = await service.SaveCustomAsync(created with { SystemPrompt = "Cite sources." });

        Assert.Equal(1, created.Version);
        Assert.Equal(2, updated.Version);
        Assert.Equal("Cite sources.", (await new SkillRepository(database).GetByIdAsync("custom"))?.SystemPrompt);
    }

    [Fact]
    public async Task SaveCustomAsync_UpdatesBuiltInSkill()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var service = new SkillService(new SkillRepository(database));
        var builtIn = await new SkillRepository(database).GetByIdAsync("builtin-chat");

        var updated = await service.SaveCustomAsync(builtIn! with { Name = "Updated chat", SystemPrompt = "Changed" });

        Assert.True(updated.IsBuiltIn);
        Assert.Equal("Changed", (await new SkillRepository(database).GetByIdAsync("builtin-chat"))?.SystemPrompt);
    }

    [Fact]
    public async Task DeleteCustomAsync_DisablesBuiltInSkill()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var service = new SkillService(new SkillRepository(database));

        await service.DeleteCustomAsync("builtin-chat");

        Assert.Null((await new SkillRepository(database).GetEnabledAsync()).FirstOrDefault(skill => skill.Id == "builtin-chat"));
        Assert.False((await new SkillRepository(database).GetByIdAsync("builtin-chat"))?.IsEnabled);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
