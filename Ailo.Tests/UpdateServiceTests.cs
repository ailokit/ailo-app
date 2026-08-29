using System.Net;
using System.Runtime.InteropServices;
using Ailo.Services;
using Microsoft.Extensions.DependencyInjection;
using AppState = Ailo.ViewModels.AppState;

namespace Ailo.Tests;

public sealed class UpdateServiceTests
{
    [Theory]
    [InlineData("0.0.6", "0.0.7", true)]
    [InlineData("v0.0.6", "v0.0.6", false)]
    [InlineData("0.1.0", "0.0.9", false)]
    public void IsNewerVersion_ComparesReleaseVersions(string current, string latest, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewerVersion(current, latest));
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReadsLatestReleaseAndSelectsPlatformAsset()
    {
        var runtime = OperatingSystem.IsWindows()
            ? RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64"
            : OperatingSystem.IsMacOS()
                ? RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64"
                : null;
        if (runtime is null)
        {
            return;
        }

        var extension = OperatingSystem.IsWindows() ? "-setup.exe" : ".dmg";
        var assetName = $"ailo-{runtime}-v9.9.9{extension}";
        var handler = new StubHandler($$"""
            {"tag_name":"v9.9.9","name":"Ailo 9.9.9","html_url":"https://github.com/ailokit/ailo-app/releases/tag/v9.9.9","draft":false,"prerelease":false,"assets":[{"name":"{{assetName}}","browser_download_url":"https://example.com/update"}]}
            """);
        using var services = new ServiceCollection().BuildServiceProvider();
        var state = new AppState(new LazyServiceProvider(services));
        var service = new UpdateService(new AppPaths(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))), state, new HttpClient(handler));
        AppUpdateInfo? raised = null;
        service.UpdateAvailable += (_, update) => raised = update;

        var updateInfo = await service.CheckForUpdateAsync();

        Assert.NotNull(updateInfo);
        Assert.Same(updateInfo, raised);
        Assert.Equal("9.9.9", updateInfo.LatestVersion);
        Assert.Equal(assetName, updateInfo.AssetName);
        Assert.Equal(UpdateState.Available, state.UpdateProgress.State);
    }

    private sealed class StubHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
    }
}
