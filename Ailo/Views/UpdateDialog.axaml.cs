using Ailo.Localization;
using Ailo.Services;

namespace Ailo.Views;

public enum UpdateDialogMode
{
    Download,
    Install
}

public partial class UpdateDialog : Window
{
    private readonly LocalizationService _localization = null!;

    public UpdateDialog() => InitializeComponent();

    public UpdateDialog(AppUpdateInfo update, LocalizationService localization, UpdateDialogMode mode)
    {
        _localization = localization;
        InitializeComponent();

        Title = _localization[mode == UpdateDialogMode.Download ? "UpdateDownloadTitle" : "UpdateInstallTitle"];
        MessageText.Text = _localization[mode == UpdateDialogMode.Download ? "UpdateDownloadMessage" : "UpdateInstallMessage"];
        VersionText.Text = string.Format(_localization["UpdateVersionDetails"], update.CurrentVersion, update.LatestVersion);
        ReleaseText.Text = string.Format(_localization["ReleaseDetails"], update.ReleaseName);
        LaterButton.Content = _localization["Later"];
        UpdateButton.Content = _localization[mode == UpdateDialogMode.Download ? "DownloadNow" : "UpdateNow"];
    }

    private void OnLaterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(false);

    private void OnUpdateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(true);
}
