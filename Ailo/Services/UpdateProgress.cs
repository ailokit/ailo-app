namespace Ailo.Services;

public enum UpdateState
{
    Idle,
    Checking,
    Available,
    Downloading,
    Ready,
    UpToDate,
    Failed
}

public sealed record UpdateProgress(
    UpdateState State,
    AppUpdateInfo? Update = null,
    double? Progress = null,
    string? ErrorMessage = null,
    string? DownloadedFilePath = null);
