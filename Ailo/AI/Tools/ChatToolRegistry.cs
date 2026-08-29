using Microsoft.Extensions.AI;

namespace Ailo.AI.Tools;

/// <summary>
/// Collects <see cref="ChatToolRegistration"/> instances and provides lookup
/// for tool-call notice formatting. Registered as a singleton and populated
/// during DI composition.
/// </summary>
public sealed class ChatToolRegistry
{
    private readonly IEnumerable<IChatToolProvider> _toolProviders;
    private readonly Lazy<Task<List<ChatToolRegistration>>> _registrations;

    public ChatToolRegistry(IEnumerable<IChatToolProvider> toolProviders)
    {
        _toolProviders = toolProviders;
        _registrations = new Lazy<Task<List<ChatToolRegistration>>>(GetAiToolsInternal);
    }

    public async Task<IList<AITool>> GetTools()
    {
        return await GetTools(null).ConfigureAwait(false);
    }

    public async Task<IList<AITool>> GetTools(IReadOnlySet<string>? enabledToolNames)
    {
        var registrations = await GetRegistrations(enabledToolNames).ConfigureAwait(false);
        return [.. registrations.Select(r => r.Tool)];
    }

    public async Task<IReadOnlyList<ChatToolRegistration>> GetRegistrations(IReadOnlySet<string>? enabledToolNames = null)
    {
        var registrations = await _registrations.Value;
        return enabledToolNames is null
            ? registrations
            : registrations.Where(registration => enabledToolNames.Contains(registration.Name)).ToArray();
    }

    private async Task<List<ChatToolRegistration>> GetAiToolsInternal()
    {
        var aiTools = new List<ChatToolRegistration>();
        foreach (var provider in _toolProviders)
        {
            var tools = await provider.GetTools();
            aiTools.AddRange(tools);
        }

        return aiTools;
    }

    /// <summary>
    /// Looks up the format-notice callback for <paramref name="toolCall"/> by its
    /// <see cref="FunctionCallContent.Name"/>. Falls back to a generic argument
    /// listing when no callback is registered.
    /// </summary>
    public Task<string> FormatNotice(FunctionCallContent toolCall) => FormatNotice(toolCall, null);

    public async Task<string> FormatNotice(FunctionCallContent toolCall, IReadOnlyList<ChatToolRegistration>? additionalRegistrations)
    {
        var registrations = await _registrations.Value;
        var registration = registrations.Concat(additionalRegistrations ?? []).FirstOrDefault(r =>
            string.Equals(r.Name, toolCall.Name, StringComparison.Ordinal));

        if (registration?.FormatNotice is { } format)
        {
            return format(toolCall);
        }

        // Generic fallback: list all arguments.
        var args = toolCall.Arguments;
        if (args is { Count: > 0 })
        {
            var argsText = string.Join(", ", args.Select(kv => $"{kv.Key}: {kv.Value}"));
            return $"Calling tool {toolCall.Name} ({argsText})...";
        }

        return $"Calling tool {toolCall.Name}...";
    }
}
