using Microsoft.Extensions.AI;

namespace Ailo.AI.Tools;

/// <summary>
/// Bundles an AI tool with optional display metadata so the chat UI can render
/// informative tool-call notices without hard-coding tool names.
/// </summary>
public sealed class ChatToolRegistration
{
    /// <summary>The name the model uses to invoke this tool (e.g. "fetch_webpage_content").</summary>
    public string Name { get; }

    /// <summary>The AI tool instance registered with the agent.</summary>
    public AITool Tool { get; }

    /// <summary>
    /// Optional callback that produces a user-facing notice when the model calls this tool.
    /// Receives the full <see cref="FunctionCallContent"/> so it can surface key arguments
    /// (e.g. the URL being fetched). When <c>null</c>, the registry falls back to a generic
    /// argument listing.
    /// </summary>
    public Func<FunctionCallContent, string>? FormatNotice { get; }

    public ChatToolRegistration(string name, AITool tool, Func<FunctionCallContent, string>? formatNotice = null)
    {
        Name = name;
        Tool = tool;
        FormatNotice = formatNotice;
    }
}
