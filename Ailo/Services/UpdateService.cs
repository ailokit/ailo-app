using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ailo.Logging;
using Microsoft.Extensions.Logging;

namespace Ailo.Services;

public sealed class UpdateService : IUpdateService
{
    private const string Repository = "ailokit/ailo-app";
    private static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{Repository}/releases/latest");
    private readonly HttpClient _httpClient;
    private readonly ViewModels.AppState _state;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);

    public UpdateService(AppPaths _, ViewModels.AppState state, HttpClient? httpClient = null)
    {
        _state = state;
        _state.CurrentVersion = CurrentVersion;
        _state.ConfigureUpdateChecker(CheckForUpdateAsync);
        _httpClient = httpClient ?? new HttpClient();
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Ailo", CurrentVersion));
        }
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public string CurrentVersion => GetCurrentVersion();

    public event EventHandler<AppUpdateInfo>? UpdateAvailable;

    public async Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        Publish(new(UpdateState.Checking));
        try
        {
            var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
            if (release is null || release.Draft || release.Prerelease || !TryParseVersion(release.TagName, out var latestVersion))
            {
                Publish(new(UpdateState.UpToDate));
                return null;
            }

            var currentVersionText = CurrentVersion;
            if (!TryParseVersion(currentVersionText, out var currentVersion) || latestVersion <= currentVersion)
            {
                Publish(new(UpdateState.UpToDate));
                return null;
            }

            var runtimeAsset = GetRuntimeAssetName(release.TagName, latestVersion);
            var asset = release.Assets.FirstOrDefault(item => string.Equals(item.Name, runtimeAsset, StringComparison.OrdinalIgnoreCase));
            if (asset is null)
            {
                Publish(new(UpdateState.UpToDate));
                return null;
            }

            var update = new AppUpdateInfo(currentVersionText, latestVersion.ToString(3), release.Name ?? release.TagName, release.HtmlUrl, asset.Name, asset.BrowserDownloadUrl);
            Publish(new(UpdateState.Available, update));
            UpdateAvailable?.Invoke(this, update);
            return update;
        }
        catch (OperationCanceledException exception)
        {
            ExceptionLogger.Log(exception, nameof(UpdateService), "Update check was cancelled", LogLevel.Information);
            throw;
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(UpdateService), "Update check failed");
            Publish(new(UpdateState.Failed, ErrorMessage: exception.Message));
            throw;
        }
    }

    public async Task<string> DownloadAsync(AppUpdateInfo update, CancellationToken cancellationToken = default)
    {
        await _downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state.UpdateProgress.State == UpdateState.Ready &&
                string.Equals(_state.UpdateProgress.Update?.AssetName, update.AssetName, StringComparison.OrdinalIgnoreCase) &&
                _state.UpdateProgress.DownloadedFilePath is { } existingPath && File.Exists(existingPath))
            {
                return existingPath;
            }

            var directory = Path.Combine(Path.GetTempPath(), "Ailo", "updates");
            Directory.CreateDirectory(directory);
            var fileName = Path.GetFileName(update.AssetName);
            var destination = Path.Combine(directory, fileName);

            try
            {
                using var response = await _httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength;
                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long downloadedBytes = 0;
                int bytesRead;
                Publish(new(UpdateState.Downloading, update, totalBytes is > 0 ? 0 : null));
                while ((bytesRead = await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    downloadedBytes += bytesRead;
                    Publish(new(UpdateState.Downloading, update, totalBytes is > 0 ? (double)downloadedBytes / totalBytes.Value : null));
                }

                Publish(new(UpdateState.Ready, update, 1, DownloadedFilePath: destination));
                return destination;
            }
            catch (OperationCanceledException exception)
            {
                ExceptionLogger.Log(exception, nameof(UpdateService), "Update download was cancelled", LogLevel.Information);
                throw;
            }
            catch (Exception exception)
            {
                ExceptionLogger.Log(exception, nameof(UpdateService), "Update download failed");
                Publish(new(UpdateState.Failed, update, ErrorMessage: exception.Message));
                throw;
            }
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    public void LaunchInstaller(string downloadedFilePath)
    {
        Process.Start(new ProcessStartInfo(downloadedFilePath) { UseShellExecute = true });
    }

    public static bool IsNewerVersion(string currentVersion, string latestVersion) =>
        TryParseVersion(currentVersion, out var current) && TryParseVersion(latestVersion, out var latest) && latest > current;

    internal static string? GetRuntimeAssetName(string tagName, Version version)
    {
        var runtime = OperatingSystem.IsWindows()
            ? RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64"
            : OperatingSystem.IsMacOS()
                ? RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64"
                : null;

        if (runtime is null)
        {
            return null;
        }

        var tag = tagName.StartsWith('v') ? tagName : $"v{version:0.0.0}";
        return OperatingSystem.IsWindows()
            ? $"ailo-{runtime}-{tag}-setup.exe"
            : $"ailo-{runtime}-{tag}.dmg";
    }

    private async Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, AI.AiloJsonSerializerContext.Default.GitHubRelease, cancellationToken).ConfigureAwait(false);
    }

    private static string GetCurrentVersion()
    {
        var informationalVersion = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informationalVersion?.Split('+')[0] is { Length: > 0 } version ? version : "0.0.0";
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim().TrimStart('v', 'V').Split('+')[0].Split('-')[0];
        if (Version.TryParse(normalized, out var parsed) && parsed is not null)
        {
            version = parsed;
            return true;
        }

        return false;
    }

    private void Publish(UpdateProgress progress)
    {
        _state.UpdateProgress = progress;
    }

    internal sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;
        [JsonPropertyName("draft")]
        public bool Draft { get; set; }
        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }
        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = [];
    }

    internal sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
