namespace Ailo.Services;

public interface IUpdateService
{
    string CurrentVersion { get; }

    event EventHandler<AppUpdateInfo>? UpdateAvailable;

    Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    Task<string> DownloadAsync(AppUpdateInfo update, CancellationToken cancellationToken = default);

    void LaunchInstaller(string downloadedFilePath);
}
