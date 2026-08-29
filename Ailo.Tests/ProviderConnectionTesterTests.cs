using System.Net;
using Ailo.AI.Providers;
using Ailo.Models;

namespace Ailo.Tests;

public sealed class ProviderConnectionTesterTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authentication_failed")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "service_unavailable")]
    public async Task TestAsync_MapsProviderHttpFailures(HttpStatusCode statusCode, string expectedCode)
    {
        var handler = new StubHandler(statusCode);
        var tester = new ProviderConnectionTester(new HttpClient(handler));

        var result = await tester.TestAsync(CreateProvider());

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(HttpMethod.Get, handler.Request?.Method);
        Assert.Equal("Bearer", handler.Request?.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task TestAsync_UsesOllamaTagsEndpointWithoutAuthorizationHeader()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var tester = new ProviderConnectionTester(new HttpClient(handler));

        var result = await tester.TestAsync(CreateProvider(ProviderType.Ollama, "http://localhost:11434"));

        Assert.True(result.IsSuccess);
        Assert.Equal("/api/tags", handler.Request?.RequestUri?.AbsolutePath);
        Assert.Null(handler.Request?.Headers.Authorization);
    }

    private static ApiProvider CreateProvider(ProviderType type = ProviderType.OpenAi, string? endpoint = null) => new("provider", "Provider", type, "key", endpoint, "model", true, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class StubHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}
