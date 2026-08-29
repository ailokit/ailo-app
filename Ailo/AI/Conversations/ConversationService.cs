using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ailo.AI.Providers;
using Ailo.AI.Skills;
using Ailo.Models;

namespace Ailo.AI.Conversations;

/// <summary>
/// Creates conversations and produces immutable snapshots used to validate persisted agent sessions.
/// </summary>
public sealed class ConversationService(ConversationRepository conversations)
{
    /// <summary>Maximum characters retained from the first user message for a conversation title.</summary>
    private const int ConversationTitleMaxLength = 30;

    /// <summary>Creates a compact title from the first user message.</summary>
    public static string CreateTitle(string message)
    {
        var title = message.Trim();
        return title.Length <= ConversationTitleMaxLength
            ? title
            : string.Concat(title.AsSpan(0, ConversationTitleMaxLength), "...");
    }

    /// <summary>
    /// Persists a new conversation with a provider and skill snapshot so later configuration changes can be detected.
    /// </summary>
    public async Task<Conversation> CreateAsync(ApiProvider provider, Skill? skill, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var now = DateTimeOffset.UtcNow;
        var configuration = JsonSerializer.Serialize(new ProviderSnapshot(provider.ProviderType, provider.ModelId, provider.Endpoint, skill?.SystemPrompt), AI.AiloJsonSerializerContext.Default.ProviderSnapshot);
        var conversation = new Conversation(
            Guid.NewGuid().ToString("N"),
            skill?.Name ?? "New conversation",
            provider.Id,
            skill?.Id,
            configuration,
            skill?.Version,
            "maf-chat-client-agent",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuration))),
            typeof(ConversationService).Assembly.GetName().Version?.ToString() ?? "unknown",
            "{}",
            SessionStatus.Active,
            false,
            now,
            now);
        await conversations.SaveAsync(conversation, cancellationToken).ConfigureAwait(false);
        return conversation;
    }

    /// <summary>Builds the compatibility snapshot that was persisted with the conversation.</summary>
    public static SessionSnapshot GetPersistedSessionSnapshot(Conversation conversation)
    {
        var configuration = JsonSerializer.Deserialize(conversation.ProviderConfiguration, AI.AiloJsonSerializerContext.Default.ProviderSnapshot) ?? throw new InvalidOperationException("Conversation provider snapshot is invalid.");
        return new SessionSnapshot(conversation.AgentType, conversation.ProviderId, configuration.ModelId, conversation.SkillVersion, conversation.MafVersion, conversation.AgentConfigurationHash, conversation.SessionState);
    }

    /// <summary>Builds the compatibility snapshot requested by the currently configured provider.</summary>
    public static SessionSnapshot GetRequestedSessionSnapshot(Conversation conversation, ApiProvider provider)
    {
        var configuration = JsonSerializer.Deserialize(conversation.ProviderConfiguration, AI.AiloJsonSerializerContext.Default.ProviderSnapshot) ?? throw new InvalidOperationException("Conversation provider snapshot is invalid.");
        var model = provider.ModelIds.Contains(configuration.ModelId, StringComparer.OrdinalIgnoreCase) ? configuration.ModelId : provider.ModelId;
        var currentConfiguration = JsonSerializer.Serialize(new ProviderSnapshot(provider.ProviderType, model, provider.Endpoint, configuration.SystemPrompt), AI.AiloJsonSerializerContext.Default.ProviderSnapshot);
        return new SessionSnapshot(conversation.AgentType, provider.Id, model, conversation.SkillVersion, conversation.MafVersion, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(currentConfiguration))), conversation.SessionState);
    }
}

internal sealed record ProviderSnapshot(ProviderType ProviderType, string ModelId, string? Endpoint, string? SystemPrompt);
