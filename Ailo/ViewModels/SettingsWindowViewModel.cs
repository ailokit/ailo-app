using CommunityToolkit.Mvvm.ComponentModel;
using Ailo.Localization;

namespace Ailo.ViewModels;

/// <summary>
/// Presentation model for the settings window. Each <see cref="Views.SettingsWindow"/> receives
/// its own instance via transient injection within a dedicated DI scope.
/// </summary>
public sealed partial class SettingsWindowViewModel : ViewModelBase
{
    /// <summary>Stable page identifiers used by settings navigation and deep links.</summary>
    public const string GeneralSettingsPage = "general";
    public const string ApiKeySettingsPage = "api-key";
    public const string SkillsSettingsPage = "skills";
    public const string ToolsSettingsPage = "tools";
    public const string McpSettingsPage = "mcp";
    public const string ShortcutSettingsPage = "shortcut";
    public const string HistorySettingsPage = "history";
    public const string ScheduledJobsSettingsPage = "scheduled-jobs";
    public const string AboutSettingsPage = "about";

    private readonly LocalizationService _localization;

    public SettingsWindowViewModel(LocalizationService localization)
    {
        _localization = localization;
    }

    public LocalizationService Localization => _localization;

    [ObservableProperty]
    private string _selectedSettingsPageKey = GeneralSettingsPage;

    public void SelectSettingsPage(string pageKey)
    {
        if (string.IsNullOrWhiteSpace(pageKey)) return;
        SelectedSettingsPageKey = pageKey;
    }

    public override void Dispose()
    {
        SelectedSettingsPageKey = GeneralSettingsPage;
        base.Dispose();
    }
}
