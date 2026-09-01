using Ailo.AI.Skills;
using Ailo.AI;
using Ailo.Data;
using Ailo.Services;
using System.Text.Json;

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
    public async Task RefreshAsync_DiscoversSkillsFromWorkingDirectoryStandardPath()
    {
        var workingDirectory = Path.Combine(_root, "workspace");
        var workspaceSkills = Path.Combine(workingDirectory, ".ailo", "skills");
        WriteSkill(workspaceSkills, "workspace-skill", "A skill from the current workspace.");
        var service = new AgentSkillsService(
            Path.Combine(_root, "skills-availability.json"),
            [new AgentSkillSourceDirectory("Ailo", Path.Combine(_root, "user-skills"))]);

        var skill = Assert.Single(await service.RefreshAsync(workingDirectory));

        Assert.Equal("workspace-skill", skill.Name);
        Assert.Equal("Current workspace", skill.Source);
        Assert.Equal(Path.GetFullPath(workspaceSkills), skill.SourceRoot);
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

    [Fact]
    public async Task InstallAsync_CreatesSelectedTypeDirectoryStructure()
    {
        var repositoryRoot = Path.Combine(_root, "repository");
        WriteSkill(repositoryRoot, "release-notes", "Prepare release notes.", withScript: true);
        var candidate = new AgentSkillInstallCandidate(
            "candidate-1", "release-notes", "Prepare release notes.", "release-notes");
        using var repository = new AgentSkillRepositoryScan(
            "https://github.com/example/skills.git",
            repositoryRoot,
            [candidate]);
        var service = new AgentSkillsService(
            Path.Combine(_root, "skills-availability.json"),
            [new AgentSkillSourceDirectory("Ailo", Path.Combine(_root, "ailo-skills"))]);
        var installBase = Path.Combine(_root, "custom-install-root");

        var installed = await service.InstallAsync(
            repository,
            [candidate.Id],
            Assert.Single(service.InstallTypes),
            installBase);

        Assert.Equal(candidate, Assert.Single(installed));
        Assert.True(File.Exists(Path.Combine(installBase, ".ailo", "skills", "release-notes", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(installBase, ".ailo", "skills", "release-notes", "convert.py")));
        var metadata = await File.ReadAllTextAsync(Path.Combine(installBase, ".ailo", "skills", "release-notes", ".ailo-skill.json"));
        Assert.Contains("https://github.com/example/skills.git", metadata);
    }

    [Fact]
    public async Task RefreshAsync_DiscoversPersistedCustomDirectories()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        var database = new SqliteDatabase(paths.DatabasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        var settings = new AppSettingsService(new AppSettingRepository(database));
        var customRoot = Path.Combine(_root, "my-skills");
        var secondCustomRoot = Path.Combine(_root, "another-my-skills");
        WriteSkill(customRoot, "custom-skill", "A custom skill.");
        WriteSkill(secondCustomRoot, "another-custom-skill", "Another custom skill.");
        await settings.SaveAsync(
            AppSettingsService.CustomAgentSkillsDirectoriesKey,
            JsonSerializer.Serialize(new[] { customRoot, secondCustomRoot }, AiloJsonSerializerContext.Default.StringArray));

        var service = new AgentSkillsService(paths, settings);
        var skills = (await service.RefreshAsync()).Where(item => item.Source == "Custom").ToArray();

        Assert.Equal(2, skills.Length);
        Assert.Contains(skills, skill => skill.Name == "custom-skill" && skill.SourceRoot == Path.GetFullPath(customRoot));
        Assert.Contains(skills, skill => skill.Name == "another-custom-skill" && skill.SourceRoot == Path.GetFullPath(secondCustomRoot));
    }

    [Fact]
    public async Task InstallAsync_AppendsSelectedCustomDirectoryToPersistedDirectories()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        var database = new SqliteDatabase(paths.DatabasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        var settings = new AppSettingsService(new AppSettingRepository(database));
        var firstCustomRoot = Path.Combine(_root, "first-custom");
        await settings.SaveAsync(
            AppSettingsService.CustomAgentSkillsDirectoriesKey,
            JsonSerializer.Serialize(new[] { firstCustomRoot }, AiloJsonSerializerContext.Default.StringArray));

        var repositoryRoot = Path.Combine(_root, "repository");
        WriteSkill(repositoryRoot, "custom-install", "A custom install skill.");
        using var repository = new AgentSkillRepositoryScan(
            "https://github.com/example/custom-skills.git",
            repositoryRoot,
            [new AgentSkillInstallCandidate("candidate-1", "custom-install", "A custom install skill.", "custom-install")]);
        var service = new AgentSkillsService(paths, settings);
        await service.RefreshAsync();
        var customType = Assert.Single(service.InstallTypes, type => type.Name == "Ailo");
        var secondCustomRoot = Path.Combine(_root, "second-custom");

        await service.InstallAsync(repository, ["candidate-1"], customType, secondCustomRoot);

        Assert.True(File.Exists(Path.Combine(secondCustomRoot, ".ailo", "skills", "custom-install", "SKILL.md")));
        Assert.Equal(
            [Path.GetFullPath(firstCustomRoot), Path.GetFullPath(secondCustomRoot)],
            service.CustomSkillsDirectories);
        var saved = await settings.GetAsync(AppSettingsService.CustomAgentSkillsDirectoriesKey);
        Assert.Equal(
            [Path.GetFullPath(firstCustomRoot), Path.GetFullPath(secondCustomRoot)],
            JsonSerializer.Deserialize(saved!, AiloJsonSerializerContext.Default.StringArray) ?? []);
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
