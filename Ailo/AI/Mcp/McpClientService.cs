using System.Text.Json;
using Ailo.AI.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Ailo.AI.Mcp;

/// <summary>Creates short-lived MCP client sessions for one agent run and refreshes persisted tool metadata.</summary>
public sealed class McpClientService(
    McpServerRepository servers,
    ILoggerFactory loggerFactory,
    ILogger<McpClientService> logger)
{
    public async Task<McpToolSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var registrations = new List<ChatToolRegistration>();
        var clients = new List<McpClient>();
        foreach (var server in await servers.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!server.IsEnabled)
            {
                continue;
            }

            try
            {
                var client = await ConnectAsync(server, cancellationToken).ConfigureAwait(false);
                clients.Add(client);
                var discoveredTools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                var configuredTools = await servers.GetToolsAsync(server.Id, cancellationToken).ConfigureAwait(false);
                var enabledNames = configuredTools
                    .Where(tool => tool.IsEnabled)
                    .Select(tool => tool.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var tool in discoveredTools)
                {
                    if (configuredTools.Count > 0 && !enabledNames.Contains(tool.Name))
                    {
                        continue;
                    }

                    var exposedName = BuildExposedName(server.Id, tool.Name);
                    var exposedTool = tool.WithName(exposedName);
                    registrations.Add(new ChatToolRegistration(
                        exposedName,
                        exposedTool,
                        call => FormatNotice(server, tool.Name, call)));
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "MCP server {ServerName} could not be connected; continuing without its tools", server.Name);
            }
        }

        return new McpToolSession(registrations, clients);
    }

    public async Task<IReadOnlyList<McpTool>> RefreshToolsAsync(McpServer server, CancellationToken cancellationToken = default)
    {
        await using var client = await ConnectAsync(server, cancellationToken).ConfigureAwait(false);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await servers.ReplaceToolsAsync(
            server.Id,
            tools.Select(tool => (Name: tool.Name, Description: (string?)tool.Description)).ToArray(),
            cancellationToken).ConfigureAwait(false);
        return await servers.GetToolsAsync(server.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpClient> ConnectAsync(McpServer server, CancellationToken cancellationToken)
    {
        IClientTransport transport = server.Transport switch
        {
            McpTransportKind.Stdio => CreateStdioTransport(server),
            McpTransportKind.StreamableHttp => CreateHttpTransport(server),
            _ => throw new InvalidOperationException($"Unsupported MCP transport '{server.Transport}'.")
        };

        try
        {
            return await McpClient.CreateAsync(
                transport,
                new McpClientOptions
                {
                    ClientInfo = new Implementation { Name = "Ailo", Version = "0.1" }
                },
                loggerFactory,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (transport is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    private IClientTransport CreateStdioTransport(McpServer server)
    {
        if (string.IsNullOrWhiteSpace(server.Command))
        {
            throw new InvalidOperationException($"MCP server '{server.Name}' requires a command.");
        }

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = server.Name,
            Command = server.Command,
            Arguments = ParseStringArray(server.ArgumentsJson),
            EnvironmentVariables = ParseStringMap(server.EnvironmentJson),
            StandardErrorLines = line => logger.LogDebug("MCP {ServerName}: {Line}", server.Name, line)
        }, loggerFactory);
    }

    private IClientTransport CreateHttpTransport(McpServer server)
    {
        if (!Uri.TryCreate(server.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"MCP server '{server.Name}' requires a valid HTTP endpoint.");
        }

        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = server.Name,
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.AutoDetect,
            AdditionalHeaders = ParseRequiredStringMap(server.HeadersJson)
        }, loggerFactory);
    }

    private static string[] ParseStringArray(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("MCP arguments must be a JSON array.");
            }

            return document.RootElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("MCP arguments must be a JSON string array.", exception);
        }
    }

    private static Dictionary<string, string?> ParseStringMap(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("MCP maps must be JSON objects.");
            }

            return document.RootElement.EnumerateObject().ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind is JsonValueKind.Null ? null : property.Value.GetString(),
                StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("MCP environment variables and headers must be JSON string maps.", exception);
        }
    }

    private static Dictionary<string, string> ParseRequiredStringMap(string json) =>
        ParseStringMap(json)
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);

    internal static string BuildExposedName(string serverId, string toolName)
    {
        var safeServerId = new string(serverId.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
        var safeToolName = new string(toolName.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
        var result = $"mcp_{safeServerId}_{safeToolName}";
        return result.Length <= 64 ? result : result[..64];
    }

    private static string FormatNotice(McpServer server, string toolName, FunctionCallContent call)
    {
        var arguments = call.Arguments is { Count: > 0 }
            ? $"（{string.Join(", ", call.Arguments.Select(argument => $"{argument.Key}: {argument.Value}"))}）"
            : string.Empty;
        return $"MCP：{server.Name} / {toolName}{arguments}";
    }
}

public sealed class McpToolSession(
    IReadOnlyList<ChatToolRegistration> registrations,
    IReadOnlyList<McpClient> clients) : IAsyncDisposable
{
    public IReadOnlyList<ChatToolRegistration> Registrations { get; } = registrations;

    public async ValueTask DisposeAsync()
    {
        foreach (var client in clients.Reverse())
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
