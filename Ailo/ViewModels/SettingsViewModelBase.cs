using CommunityToolkit.Mvvm.ComponentModel;
using Ailo.Localization;

namespace Ailo.ViewModels;

/// <summary>
/// Base class for settings page ViewModels with shared localization and status support.
/// </summary>
public abstract partial class SettingsViewModelBase : ViewModelBase
{
    private readonly LocalizationService _localization;

    protected SettingsViewModelBase(LocalizationService localization)
    {
        _localization = localization;
    }

    public LocalizationService Localization => _localization;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    protected string T(string key) => _localization[key];
}
