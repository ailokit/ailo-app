using System.Net;
using System.Text;
using Ailo.Logging;
using Microsoft.Extensions.Logging;

namespace Ailo.AI.Providers;

public sealed class ProviderConnectionTester(HttpClient? httpClient = null) : IProviderConnectionTester
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    public async Task<ProviderConnectionResult> TestAsync(ApiProvider provider, CancellationToken cancellationToken = default)
    {
        try
        {
            ProviderService.Validate(provider);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var request = CreateRequest(provider);
            using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? ProviderConnectionResult.Success() : MapStatusCode(response.StatusCode);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            ExceptionLogger.Log(exception, nameof(ProviderConnectionTester), "Provider connection test timed out", LogLevel.Warning);
            return new ProviderConnectionResult(false, "request_timeout", "The connection test timed out.");
        }
        catch (HttpRequestException exception)
        {
            ExceptionLogger.Log(exception, nameof(ProviderConnectionTester), "Provider connection test failed", LogLevel.Warning);
            return new ProviderConnectionResult(false, "network_error", "The provider endpoint could not be reached.");
        }
        catch (ArgumentException exception)
        {
            ExceptionLogger.Log(exception, nameof(ProviderConnectionTester), "Provider connection configuration is invalid", LogLevel.Warning);
            return new ProviderConnectionResult(false, "invalid_configuration", exception.Message);
        }
    }

    private static HttpRequestMessage CreateRequest(ApiProvider provider)
    {
        if (provider.ProviderType == ProviderType.Anthropic)
        {
            var endpoint = CombineEndpoint(provider.Endpoint ?? "https://api.anthropic.com", "v1/messages");
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent($"{{\"model\":\"{provider.ModelId}\",\"max_tokens\":1,\"messages\":[{{\"role\":\"user\",\"content\":\"ping\"}}]}}", Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-api-key", provider.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            return request;
        }

        var path = provider.ProviderType == ProviderType.Ollama ? "api/tags" : "models";
        var baseEndpoint = provider.Endpoint ?? "https://api.openai.com/v1";
        var openAiRequest = new HttpRequestMessage(HttpMethod.Get, CombineEndpoint(baseEndpoint, path));
        if (provider.ProviderType != ProviderType.Ollama)
        {
            openAiRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", provider.ApiKey);
        }

        return openAiRequest;
    }

    private static Uri CombineEndpoint(string baseEndpoint, string path) => new(new Uri(baseEndpoint.TrimEnd('/') + "/", UriKind.Absolute), path);

    private static ProviderConnectionResult MapStatusCode(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(false, "authentication_failed", "Authentication failed. Check the API key."),
        HttpStatusCode.TooManyRequests => new(false, "rate_limited", "The provider is rate-limiting requests. Try again later."),
        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => new(false, "service_unavailable", "The provider is temporarily unavailable. Try again later."),
        _ => new(false, "unexpected_response", $"The provider returned HTTP {(int)statusCode}.")
    };

    public async Task<IReadOnlyList<string>> FetchModelsAsync(ApiProvider provider, CancellationToken cancellationToken = default)
    {
        ProviderService.Validate(provider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = CreateRequest(provider);
        using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return provider.ProviderType == ProviderType.Ollama
            ? ParseOllamaModels(doc)
            : ParseOpenAiModels(doc);
    }

    private static IReadOnlyList<string> ParseOpenAiModels(System.Text.Json.JsonDocument doc)
    {
        var models = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                {
                    models.Add(id.GetString() ?? string.Empty);
                }
            }
        }

        return models;
    }

    private static IReadOnlyList<string> ParseOllamaModels(System.Text.Json.JsonDocument doc)
    {
        var models = new List<string>();
        if (doc.RootElement.TryGetProperty("models", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var name))
                {
                    models.Add(name.GetString() ?? string.Empty);
                }
            }
        }

        return models;
    }
}
