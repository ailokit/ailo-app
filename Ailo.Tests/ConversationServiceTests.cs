using Ailo.AI.Conversations;
using Ailo.AI.Providers;
using Ailo.AI.Skills;
using Ailo.Data;

namespace Ailo.Tests;

public sealed class ConversationServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public void CreateTitle_TruncatesLongFirstMessage()
    {
        var title = ConversationService.CreateTitle("12345678901234567890123456789012345");

        Assert.Equal("123456789012345678901234567890...", title);
    }

    [Fact]
    public async Task CreateAsync_PersistsImmutableProviderAndSkillSnapshot()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var provider = new ApiProvider("provider", "OpenAI", ProviderType.OpenAi, "key", "https://example.test", "gpt-test", true, true, now, now);
        await new ApiProviderRepository(database).SaveAsync(provider);
        var skill = new Skill("skill", "Translation", null, "Translate faithfully.", null, false, true, 0, 3, now, now);
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Skills (Id, Name, SystemPrompt, IsBuiltIn, IsEnabled, SortOrder, Version, CreatedAt, UpdatedAt) VALUES ($id, $name, $prompt, 0, 1, 0, $version, $createdAt, $updatedAt);";
            command.Parameters.AddWithValue("$id", skill.Id);
            command.Parameters.AddWithValue("$name", skill.Name);
            command.Parameters.AddWithValue("$prompt", skill.SystemPrompt);
            command.Parameters.AddWithValue("$version", skill.Version);
            command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
            command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
        var service = new ConversationService(new ConversationRepository(database));

        var conversation = await service.CreateAsync(provider, skill);
        var persisted = await new ConversationRepository(database).GetByIdAsync(conversation.Id);

        Assert.NotNull(persisted);
        Assert.Equal(provider.Id, persisted.ProviderId);
        Assert.Equal(skill.Id, persisted.SkillId);
        Assert.Equal(skill.Version, persisted.SkillVersion);
        Assert.Equal("{}", persisted.SessionState);
        Assert.Equal(SessionStatus.Active, persisted.SessionStatus);
        Assert.Contains("gpt-test", persisted.ProviderConfiguration);
        Assert.Contains("Translate faithfully.", persisted.ProviderConfiguration);
        Assert.NotEmpty(persisted.AgentConfigurationHash);
    }

    [Fact]
    public async Task CreateAsync_PersistsSkillPromptInProviderSnapshot()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var provider = new ApiProvider("provider", "OpenAI", ProviderType.OpenAi, "key", null, "model", true, true, now, now);
        await new ApiProviderRepository(database).SaveAsync(provider);
        var skill = new Skill("skill", "Translate", null, "Always answer in Spanish.", null, false, true, 0, 1, now, now);
        await new SkillRepository(database).SaveAsync(skill);
        var conversation = await new ConversationService(new ConversationRepository(database)).CreateAsync(provider, skill);

        Assert.Contains("Always answer in Spanish.", conversation.ProviderConfiguration);
    }

    [Fact]
    public async Task SessionSnapshots_DetectProviderModelChangeBeforeRestore()
    {
        var now = DateTimeOffset.UtcNow;
        var provider = new ApiProvider("provider", "OpenAI", ProviderType.OpenAi, "key", "https://example.test", "gpt-original", true, true, now, now);
        var conversation = new Conversation("conversation", "Conversation", provider.Id, null, "{\"ProviderType\":0,\"ModelId\":\"gpt-original\",\"Endpoint\":\"https://example.test\",\"SystemPrompt\":null}", null, "maf-chat-client-agent", "B4A8B3E13021B8C4B9F79E2F797B850E2B76D25DD02D69B16FFEEBEAB6D0B504", "1.0.0.0", "{\"state\":true}", SessionStatus.Restorable, false, now, now);

        var persisted = ConversationService.GetPersistedSessionSnapshot(conversation);
        var requested = ConversationService.GetRequestedSessionSnapshot(conversation, provider with { ModelId = "gpt-new" });

        Assert.False(SessionStateValidator.CanRestore(persisted, requested));
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
