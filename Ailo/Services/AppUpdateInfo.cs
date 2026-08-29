namespace Ailo.Services;

public sealed record AppUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string ReleaseName,
    string ReleaseUrl,
    string AssetName,
    string DownloadUrl);
