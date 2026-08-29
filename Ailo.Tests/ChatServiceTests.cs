using Ailo.AI;
using Ailo.AI.Conversations;
using Ailo.AI.Providers;
using Ailo.AI.Tools;
using Ailo.Data;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Ailo.Tests;

public sealed class ChatServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task SendStreamingAsync_UsesMafAgentAndRejectsUnsupportedProvider()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        await SeedConversationAsync(database);
        var service = new ChatService(new MessageRepository(database), new ConversationRepository(database), new ApiProviderRepository(database), new SessionRunLock(), new ChatToolRegistry([]), new ChatWorkspace());

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (var _ in service.SendStreamingAsync("conversation", "hello"))
            {
            }
        });
    }

    [Fact]
    public void BuildUserMessage_IncludesTextAndDataContent()
    {
        var dirPath = Path.Combine(Path.GetTempPath(), "Ailo.Tests");
        var imagePath = Path.Combine(dirPath, $"{Guid.NewGuid():N}.png");
        try
        {
            Directory.CreateDirectory(dirPath);
            File.WriteAllBytes(imagePath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var attachments = new[] { new MessageAttachment(imagePath, "a.png", "image/png") };

            var message = ChatService.BuildUserMessage("look", attachments);

            Assert.Equal(ChatRole.User, message.Role);
            Assert.Equal("look", message.Text);
            var data = Assert.Single(message.Contents.OfType<DataContent>());
            Assert.Equal("image/png", data.MediaType);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], data.Data.ToArray());
        }
        finally
        {
            if (File.Exists(imagePath)) File.Delete(imagePath);
        }
    }

    [Fact]
    public void AgentSessionJsonOptions_SerializeMessageSourceAttributionWithoutReflectionFallback()
    {
        var message = new ChatMessage(ChatRole.User, "hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [AgentRequestMessageSourceAttribution.AdditionalPropertiesKey] =
                    new AgentRequestMessageSourceAttribution(AgentRequestMessageSourceType.External, "test")
            }
        };
        var state = new InMemoryChatHistoryProvider.State { Messages = [message] };
        var stateBag = new AgentSessionStateBag();
        stateBag.SetValue("history", state, AiloJsonSerializerOptions.AgentSession);

        var json = stateBag.Serialize();

        Assert.Contains("sourceType", json.GetRawText());
        Assert.Contains("sourceId", json.GetRawText());
    }

    [Fact]
    public async Task SendStreamingAsync_PersistsUserMessageWithAttachments_BeforeProviderRejection()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        await SeedConversationAsync(database);
        var service = new ChatService(new MessageRepository(database), new ConversationRepository(database), new ApiProviderRepository(database), new SessionRunLock(), new ChatToolRegistry([]), new ChatWorkspace());
        var dirPath = Path.Combine(Path.GetTempPath(), "Ailo.Tests");
        var imagePath = Path.Combine(dirPath, $"{Guid.NewGuid():N}.png");
        try
        {
            Directory.CreateDirectory(dirPath);
            File.WriteAllBytes(imagePath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var attachments = new[] { new MessageAttachment(imagePath, "a.png", "image/png") };

            await Assert.ThrowsAsync<NotSupportedException>(async () =>
            {
                await foreach (var _ in service.SendStreamingAsync("conversation", "look", attachments))
                {
                }
            });

            var messages = await new MessageRepository(database).GetByConversationAsync("conversation");
            var user = Assert.Single(messages, static m => m.Role == MessageRole.User);
            Assert.Equal("look", user.Content);
            var attachment = Assert.Single(user.Attachments);
            Assert.Equal("image/png", attachment.MimeType);
            var assistant = Assert.Single(messages, static m => m.Role == MessageRole.Assistant);
            Assert.Equal(MessageStatus.Failed, assistant.Status);
            Assert.Contains("Anthropic", assistant.Content);
            Assert.Contains("Anthropic", assistant.ErrorMessage);
        }
        finally
        {
            if (File.Exists(imagePath)) File.Delete(imagePath);
        }
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        var dirPath = Path.Combine(Path.GetTempPath(), "Ailo.Tests");
        try { if (Directory.Exists(dirPath)) Directory.Delete(dirPath); } catch (IOException) { /* directory not empty yet */ }
    }

    private static async Task SeedConversationAsync(SqliteDatabase database)
    {
        await using var c = await database.OpenConnectionAsync(); await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO ApiProviders (Id,Name,ProviderType,ApiKey,ModelId,CreatedAt,UpdatedAt) VALUES ('p','p',1,'k','m',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP); INSERT INTO Conversations (Id,Title,ProviderId,ProviderConfiguration,AgentType,AgentConfigurationHash,MafVersion,SessionState,SessionStatus,CreatedAt,UpdatedAt) VALUES ('conversation','c','p','{\"ProviderType\":1,\"ModelId\":\"m\",\"Endpoint\":null,\"SystemPrompt\":null}','a','h','1','{}',0,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);";
        await cmd.ExecuteNonQueryAsync();
    }

}
