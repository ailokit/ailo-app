using System.Net;
using Ailo.AI.Tools;

namespace Ailo.Tests;

public sealed class WebContentToolTests
{
    [Fact]
    public async Task GetWebpageContentAsync_ExtractsReadableTextAndRemovesUnsafeElements()
    {
        using var client = new HttpClient(new StubHandler("<html><head><title>Example</title><script>ignore()</script></head><body><h1>Hello</h1><p>World &amp; friends</p></body></html>"));
        var tool = new WebContentTool(client);

        var result = await tool.GetWebpageContentAsync("https://1.1.1.1/example");

        Assert.Contains("Title: Example", result);
        Assert.Contains("Hello World & friends", result);
        Assert.DoesNotContain("ignore()", result);
    }

    [Fact]
    public async Task GetWebpageContentAsync_RejectsNonHttpUrls()
    {
        var tool = new WebContentTool(new HttpClient(new StubHandler("unused")));

        var result = await tool.GetWebpageContentAsync("file:///C:/secret.txt");

        Assert.Equal("The URL must be a public http or https address.", result);
    }

    private sealed class StubHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "text/html")
            });
    }
}
