using Ailo.AI;
using Ailo.AI.Conversations;
using Ailo.AI.Providers;
using Ailo.AI.Skills;
using Ailo.Data;
using Ailo.Models;
using Microsoft.Data.Sqlite;

namespace Ailo.Tests;

public sealed class RepositoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task SaveAsync_MakesOnlyLatestDefaultProviderDefault()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ApiProviderRepository(database);
        await repository.SaveAsync(CreateProvider("one", true));
        await repository.SaveAsync(CreateProvider("two", true));

        var providers = await repository.GetAllAsync();

        var defaultProvider = Assert.Single(providers, static provider => provider.IsDefault);
        Assert.Equal("two", defaultProvider.Id);
    }

    [Fact]
    public async Task SaveAsync_PersistsMultipleModelsForOneProvider()
    {
        var database = await CreateDatabaseAsync();
        var provider = CreateProvider("one", true) with { ModelIds = ["model-a", "model-b"], ModelId = "model-a" };

        await new ApiProviderRepository(database).SaveAsync(provider);

        var saved = await new ApiProviderRepository(database).GetByIdAsync(provider.Id);
        Assert.Equal(["model-a", "model-b"], saved?.ModelIds);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsMultimodalModelFlags()
    {
        var database = await CreateDatabaseAsync();
        var provider = CreateProvider("one", true) with
        {
            ModelIds = ["model-a", "model-b"],
            ModelId = "model-a",
            MultimodalModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "model-b" }
        };

        await new ApiProviderRepository(database).SaveAsync(provider);

        var saved = await new ApiProviderRepository(database).GetByIdAsync(provider.Id);
        Assert.Equal(["model-a", "model-b"], saved?.ModelIds);
        Assert.Contains("model-b", saved?.MultimodalModelIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain("model-a", saved?.MultimodalModelIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetEnabledAsync_ReturnsSeededSkillsInDisplayOrder()
    {
        var database = await CreateDatabaseAsync();

        var skills = await new SkillRepository(database).GetEnabledAsync();

        Assert.Equal(5, skills.Count);
        Assert.Equal("builtin-chat", skills[0].Id);
        Assert.All(skills, static skill => Assert.True(skill.IsBuiltIn));
    }

    [Fact]
    public async Task ReorderEnabledAsync_PersistsTheNewDisplayOrder()
    {
        var database = await CreateDatabaseAsync();
        var repository = new SkillRepository(database);
        var skills = await repository.GetEnabledAsync();

        await repository.ReorderEnabledAsync(skills.Select(skill => skill.Id).Reverse().ToArray());

        var reordered = await repository.GetEnabledAsync();
        Assert.Equal(skills.Select(skill => skill.Id).Reverse(), reordered.Select(skill => skill.Id));
    }

    [Fact]
    public async Task ConversationAndMessageQueries_ReturnPersistentHistoryInDisplayOrder()
    {
        var database = await CreateDatabaseAsync();
        var provider = CreateProvider("provider", true);
        await new ApiProviderRepository(database).SaveAsync(provider);
        var now = DateTimeOffset.UtcNow;
        var older = new Conversation("older", "Older", provider.Id, null, "{}", null, "agent", "hash", "1", "{}", SessionStatus.Active, false, now.AddMinutes(-1), now.AddMinutes(-1));
        var newer = new Conversation("newer", "Newer", provider.Id, null, "{}", null, "agent", "hash", "1", "{}", SessionStatus.Active, false, now, now);
        var conversations = new ConversationRepository(database);
        await conversations.SaveAsync(older);
        await conversations.SaveAsync(newer);
        var messages = new MessageRepository(database);
        await messages.AppendAsync(new Message("second", newer.Id, 2, MessageRole.Assistant, "second", MessageStatus.Completed, null, null, now, now));
        await messages.AppendAsync(new Message("first", newer.Id, 1, MessageRole.User, "first", MessageStatus.Completed, null, null, now, now));

        var recent = await conversations.GetRecentAsync();
        var history = await messages.GetByConversationAsync(newer.Id);

        Assert.Equal(["newer", "older"], recent.Select(conversation => conversation.Id));
        Assert.Equal(["first", "second"], history.Select(message => message.Content));
    }

    [Fact]
    public async Task ArchiveAsync_HidesConversationFromPagedHistory()
    {
        var database = await CreateDatabaseAsync();
        var provider = CreateProvider("provider", true);
        await new ApiProviderRepository(database).SaveAsync(provider);
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation("conversation", "Conversation", provider.Id, null, "{}", null, "agent", "hash", "1", "{}", SessionStatus.Active, false, now, now);
        var repository = new ConversationRepository(database);
        await repository.SaveAsync(conversation);

        await repository.ArchiveAsync(conversation.Id);

        Assert.Empty(await repository.GetPageAsync(0, 20));
        Assert.True((await repository.GetByIdAsync(conversation.Id))?.IsArchived ?? false);
    }

    [Fact]
    public async Task AppendAsync_RoundTripsMessageAttachments()
    {
        var database = await CreateDatabaseAsync();
        var provider = CreateProvider("provider", true);
        await new ApiProviderRepository(database).SaveAsync(provider);
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation("conversation", "Conversation", provider.Id, null, "{}", null, "agent", "hash", "1", "{}", SessionStatus.Active, false, now, now);
        await new ConversationRepository(database).SaveAsync(conversation);
        var repository = new MessageRepository(database);
        var message = new Message("m1", conversation.Id, 1, MessageRole.User, "look", MessageStatus.Completed, null, null, now, now)
        {
            Attachments = [new MessageAttachment("/tmp/a.png", "a.png", "image/png")]
        };

        await repository.AppendAsync(message);
        var history = await repository.GetByConversationAsync(conversation.Id);

        var saved = Assert.Single(history);
        Assert.Equal("look", saved.Content);
        var attachment = Assert.Single(saved.Attachments);
        Assert.Equal("/tmp/a.png", attachment.FilePath);
        Assert.Equal("a.png", attachment.FileName);
        Assert.Equal("image/png", attachment.MimeType);
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private async Task<SqliteDatabase> CreateDatabaseAsync()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        return database;
    }

    private static ApiProvider CreateProvider(string id, bool isDefault) => new(
        id, id, ProviderType.OpenAi, "key", null, "model", isDefault, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
