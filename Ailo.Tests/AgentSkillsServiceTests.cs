using Ailo.AI.Skills;
using Ailo.Data;

namespace Ailo.Tests;

public sealed class AgentSkillsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Ailo.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RefreshAsync_DiscoversSkillsBySourceAndPersistsAvailability()
    {
        var ailoRoot = Path.Combine(_root, "ailo-skills");
        var codexRoot = Path.Combine(_root, "codex-skills");
        WriteSkill(ailoRoot, "unit-converter", "Convert common measurement units.", withScript: true);
        WriteSkill(codexRoot, "release-notes", "Prepare concise release notes.");

        var service = new AgentSkillsService(
            Path.Combine(_root, "skills-availability.json"),
            [new AgentSkillSourceDirectory("Ailo", ailoRoot), new AgentSkillSourceDirectory("Codex", codexRoot)]);

        var discovered = await service.RefreshAsync();

        Assert.Collection(discovered.OrderBy(skill => skill.Name),
            skill =>
            {
                Assert.Equal("release-notes", skill.Name);
                Assert.Equal("Codex", skill.Source);
                Assert.False(skill.HasScripts);
                Assert.True(skill.IsEnabled);
            },
            skill =>
            {
                Assert.Equal("unit-converter", skill.Name);
                Assert.Equal("Ailo", skill.Source);
                Assert.True(skill.HasScripts);
                Assert.True(skill.IsEnabled);
            });

        foreach (var skill in discovered)
            await service.SetEnabledAsync(skill.DirectoryPath, false);

        var refreshed = await service.RefreshAsync();
        Assert.All(refreshed, skill => Assert.False(skill.IsEnabled));
        Assert.Null(await service.CreateSourceAsync());
    }

    [Fact]
    public async Task RefreshAsync_IgnoresSkillFilesWithoutRequiredFrontmatter()
    {
        var root = Path.Combine(_root, "skills");
        var invalidSkillDirectory = Path.Combine(root, "invalid");
        Directory.CreateDirectory(invalidSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(invalidSkillDirectory, "SKILL.md"), "# No frontmatter");

        var service = new AgentSkillsService(Path.Combine(_root, "skills-availability.json"), [new AgentSkillSourceDirectory("Ailo", root)]);

        Assert.Empty(await service.RefreshAsync());
    }

    [Fact]
    public async Task UninstallAsync_DeletesSkillDirectoryAndAvailabilityOverride()
    {
        var root = Path.Combine(_root, "skills");
        WriteSkill(root, "remove-me", "A skill to remove.", withScript: true);
        var availabilityPath = Path.Combine(_root, "skills-availability.json");
        var service = new AgentSkillsService(availabilityPath, [new AgentSkillSourceDirectory("Ailo", root)]);
        var skillDirectory = Path.Combine(root, "remove-me");

        await service.SetEnabledAsync(skillDirectory, false);
        await service.UninstallAsync(skillDirectory);

        Assert.False(Directory.Exists(skillDirectory));
        Assert.Empty(await service.RefreshAsync());
        Assert.DoesNotContain(skillDirectory, await File.ReadAllTextAsync(availabilityPath));
    }

    [Fact]
    public async Task UninstallAsync_RejectsSourceRoot()
    {
        var root = Path.Combine(_root, "skills");
        WriteSkill(root, "keep-me", "A skill to keep.");
        var service = new AgentSkillsService(Path.Combine(_root, "skills-availability.json"), [new AgentSkillSourceDirectory("Ailo", root)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UninstallAsync(root));

        Assert.True(Directory.Exists(root));
        Assert.NotEmpty(await service.RefreshAsync());
    }

    private static void WriteSkill(string root, string name, string description, bool withScript = false)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), $$"""
            ---
            name: {{name}}
            description: {{description}}
            ---

            Follow the skill instructions.
            """);
        if (withScript)
            File.WriteAllText(Path.Combine(directory, "convert.py"), "print('ok')");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
