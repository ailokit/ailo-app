using CommunityToolkit.Mvvm.ComponentModel;
using Ailo.Localization;
using Ailo.Services;

namespace Ailo.ViewModels;

public sealed partial class AboutSettingsViewModel : SettingsViewModelBase
{
    private readonly AppState _state;

    public AboutSettingsViewModel(AppState state, LocalizationService localization)
        : base(localization)
    {
        _state = state;
    }

    public AppState State => _state;
    public string CurrentVersion => _state.CurrentVersion;

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private double _updateProgress;

    public void LoadFromState()
    {
        var progress = _state.UpdateProgress;
        UpdateProgress = progress.Progress ?? (progress.State == UpdateState.Ready ? 1 : 0);
        IsDownloadingUpdate = progress.State == UpdateState.Downloading;
        UpdateStatus = progress.State switch
        {
            UpdateState.Checking => T("CheckingForUpdates"),
            UpdateState.Available => T("UpdateAvailable"),
            UpdateState.Downloading => T("DownloadingUpdate"),
            UpdateState.Ready => T("UpdateReady"),
            UpdateState.UpToDate => T("NoUpdates"),
            UpdateState.Failed => string.Format(T("UpdateCheckFailed"), progress.ErrorMessage ?? string.Empty),
            _ => string.Empty
        };
    }
}
