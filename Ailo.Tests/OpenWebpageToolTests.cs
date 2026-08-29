using Ailo.AI.Tools;
using Ailo.Services;

namespace Ailo.Tests;

public sealed class OpenWebpageToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Ailo.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void OpensHttpAndHttpsAddressesInSystemBrowser()
    {
        var browser = new FakeSystemBrowserService();
        var tool = CreateTool(browser);

        var result = tool.OpenWebpageInBrowser("https://example.com/path?q=value");

        Assert.Equal(new Uri("https://example.com/path?q=value"), browser.OpenedUrl);
        Assert.Contains("https://example.com/path?q=value", result);
    }

    [Theory]
    [InlineData("file:///tmp/private.txt")]
    [InlineData("mailto:user@example.com")]
    [InlineData("not a url")]
    [InlineData("https://user:password@example.com")]
    public void RejectsNonWebOrCredentialedAddresses(string url)
    {
        var browser = new FakeSystemBrowserService();
        var tool = CreateTool(browser);

        var result = tool.OpenWebpageInBrowser(url);

        Assert.Null(browser.OpenedUrl);
        Assert.DoesNotContain("Opened", result);
    }

    [Fact]
    public async Task OpensAuthorizedLocalHtmlFileByPathOrFileUri()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "report.html");
        await File.WriteAllTextAsync(path, "<h1>Report</h1>");
        var browser = new FakeSystemBrowserService();
        var tool = CreateTool(browser);

        var result = tool.OpenWebpageInBrowser(new Uri(path).AbsoluteUri);

        Assert.Equal(new Uri(WorkspacePathSecurity.NormalizeEntry(path, isDirectory: false).Path), browser.OpenedUrl);
        Assert.Contains("file:", result);
    }

    [Fact]
    public async Task RejectsLocalFilesOutsideWorkspaceOrWithoutHtmlExtension()
    {
        Directory.CreateDirectory(_root);
        var allowedText = Path.Combine(_root, "report.txt");
        var outside = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(allowedText, "text");
        await File.WriteAllTextAsync(outside, "<h1>Outside</h1>");
        var browser = new FakeSystemBrowserService();
        var tool = CreateTool(browser);

        Assert.Contains(".html", tool.OpenWebpageInBrowser(allowedText));
        Assert.Contains("selected workspace", tool.OpenWebpageInBrowser(outside));
        Assert.Null(browser.OpenedUrl);
        File.Delete(outside);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private OpenWebpageTool CreateTool(FakeSystemBrowserService browser)
    {
        var workspace = new ChatWorkspace();
        Directory.CreateDirectory(_root);
        workspace.Replace([new WorkspaceEntry(_root, IsDirectory: true)]);
        return new OpenWebpageTool(browser, workspace);
    }

    private sealed class FakeSystemBrowserService : ISystemBrowserService
    {
        public Uri? OpenedUrl { get; private set; }

        public void Open(Uri url) => OpenedUrl = url;
    }
}
