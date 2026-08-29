using System.Text.Json;
using System.Text.Json.Serialization;
using Ailo.AI.Conversations;
using Ailo.Services;
using Microsoft.Agents.AI;

namespace Ailo.AI;

[JsonSerializable(typeof(ProviderSnapshot))]
[JsonSerializable(typeof(UpdateService.GitHubRelease))]
[JsonSerializable(typeof(MessageAttachment[]))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(AgentRequestMessageSourceAttribution))]
internal sealed partial class AiloJsonSerializerContext : JsonSerializerContext;

internal static class AiloJsonSerializerOptions
{
    public static JsonSerializerOptions AgentSession { get; } = CreateAgentSessionOptions();

    private static JsonSerializerOptions CreateAgentSessionOptions()
    {
        var options = new JsonSerializerOptions(AgentAbstractionsJsonUtilities.DefaultOptions);
        options.TypeInfoResolverChain.Add(AiloJsonSerializerContext.Default);
        options.MakeReadOnly();
        return options;
    }
}
