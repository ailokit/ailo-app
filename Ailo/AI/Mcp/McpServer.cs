namespace Ailo.AI.Mcp;

public enum McpTransportKind
{
    Stdio = 0,
    StreamableHttp = 1
}

public sealed record McpServer(
    string Id,
    string Name,
    McpTransportKind Transport,
    string? Endpoint,
    string? Command,
    string ArgumentsJson,
    string EnvironmentJson,
    string HeadersJson,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record McpTool(
    string Id,
    string ServerId,
    string Name,
    string? Description,
    bool IsEnabled,
    DateTimeOffset UpdatedAt);
