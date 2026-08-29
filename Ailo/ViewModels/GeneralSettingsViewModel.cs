using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ailo.Localization;
using Ailo.Logging;
using Ailo.Services;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Ailo.ViewModels;

public sealed partial class GeneralSettingsViewModel : SettingsViewModelBase
{
    private readonly AppSettingsService _settings;
    private readonly IStartupService _startup;
    private readonly AppState _state;
    private readonly DataDirectoryService _dataDirectoryService;
    private IStorageProvider? _storageProvider;
    private bool _loadingSettings;
    private bool _syncingAppState;
    private bool _restoringStartupSetting;

    public GeneralSettingsViewModel(
        AppSettingsService settings,
        IStartupService startup,
        AppState state,
        DataDirectoryService dataDirectoryService,
        LocalizationService localization)
        : base(localization)
    {
        _settings = settings;
        _startup = startup;
        _state = state;
        _dataDirectoryService = dataDirectoryService;
        _state.PropertyChanged += OnAppStatePropertyChanged;
        UpdateSelectedAccentColor(AccentColor);
    }

    public IReadOnlyList<LanguageOption> LanguageOptions => Localization.LanguageOptions;
    public ObservableCollection<ThemeOption> ThemeOptions { get; } = [];
    public IReadOnlyList<AccentColorOption> AccentColorOptions { get; } =
    [
        new("lagoon", "Lagoon", "#0EA5E9"),
        new("cobalt", "Cobalt", "#2563EB"),
        new("periwinkle", "Periwinkle", "#6366F1"),
        new("iris", "Iris", "#8B5CF6"),
        new("fuchsia", "Fuchsia", "#C026D3"),
        new("mauve", "Mauve", "#A855F7"),
        new("petal", "Petal", "#EC4899"),
        new("blush", "Blush", "#F43F5E"),
        new("flamingo", "Flamingo", "#FB7185"),
        new("raspberry", "Raspberry", "#BE185D"),
        new("ember", "Ember", "#F97316"),
        new("honey", "Honey", "#F59E0B"),
        new("moss", "Moss", "#84CC16"),
        new("jade", "Jade", "#10B981"),
        new("teal", "Teal", "#14B8A6"),
        new("arctic", "Arctic", "#06B6D4"),
        new("slate", "Slate", "#64748B"),
        new("deep-sea", "Deep Sea", "#0F766E"),
        new("midnight", "Midnight", "#1E3A8A"),
        new("graphite", "Graphite", "#334155")
    ];

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    [ObservableProperty]
    private Color _accentColor = Color.Parse(AppSettingsService.DefaultAccentColor);

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private bool _launchAtStartup;

    [ObservableProperty]
    private string _dataDirectory = string.Empty;

    [ObservableProperty]
    private bool _migrateData = true;

    [ObservableProperty]
    private bool _isUpdatingDataDirectory;

    public void AttachStorageProvider(IStorageProvider? storageProvider) => _storageProvider = storageProvider;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var theme = _state.ThemeKey;
        var accentColor = _state.AccentColorHex;
        var language = _state.LanguageKey;
        _loadingSettings = true;
        try
        {
            RefreshThemeOptions();
            SelectedLanguage = LanguageOptions.FirstOrDefault(item => item.Key == language) ?? LanguageOptions[0];
            Localization.SetLanguage(SelectedLanguage.Key);
            SelectedTheme = ThemeOptions.FirstOrDefault(item => item.Key == theme) ?? ThemeOptions[0];
            AccentColor = AppSettingsService.TryParseColor(accentColor, out var parsedAccent)
                ? parsedAccent
                : Color.Parse(AppSettingsService.DefaultAccentColor);
            LaunchAtStartup = _state.LaunchAtStartup;
            DataDirectory = _dataDirectoryService.CurrentDataDirectory;
            MigrateData = true;
            AppSettingsService.ApplyTheme(SelectedTheme.Key);
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void RefreshThemeOptions()
    {
        ThemeOptions.Clear();
        ThemeOptions.Add(new ThemeOption("system", T("System")));
        ThemeOptions.Add(new ThemeOption("light", T("Light")));
        ThemeOptions.Add(new ThemeOption("dark", T("Dark")));
    }

    [RelayCommand]
    private void SelectAccentColor(AccentColorOption? option)
    {
        if (option is not null)
            AccentColor = option.Color;
    }

    [RelayCommand]
    private async Task BrowseDataDirectoryAsync()
    {
        if (_storageProvider is null)
        {
            return;
        }

        var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = T("SelectDataDirectory"),
            AllowMultiple = false
        });
        var selectedPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            DataDirectory = selectedPath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyDataDirectory))]
    private async Task ApplyDataDirectoryAsync()
    {
        if (string.IsNullOrWhiteSpace(DataDirectory))
        {
            return;
        }

        IsUpdatingDataDirectory = true;
        try
        {
            await _dataDirectoryService.SetDataDirectoryAsync(DataDirectory, MigrateData).ConfigureAwait(false);
            StatusMessage = T("DataDirectoryUpdatedRestartRequired");
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(GeneralSettingsViewModel), "Failed to update data directory");
            StatusMessage = exception.Message;
        }
        finally
        {
            IsUpdatingDataDirectory = false;
        }
    }

    private bool CanApplyDataDirectory() => !IsUpdatingDataDirectory && !string.IsNullOrWhiteSpace(DataDirectory);

    partial void OnDataDirectoryChanged(string value) => ApplyDataDirectoryCommand.NotifyCanExecuteChanged();

    partial void OnIsUpdatingDataDirectoryChanged(bool value) => ApplyDataDirectoryCommand.NotifyCanExecuteChanged();

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is null) return;

        Localization.SetLanguage(value.Key);
        var selectedThemeKey = SelectedTheme?.Key;
        RefreshThemeOptions();
        if (selectedThemeKey is not null)
            SelectedTheme = ThemeOptions.First(item => item.Key == selectedThemeKey);

        if (!_loadingSettings && !_syncingAppState)
        {
            _state.LanguageKey = value.Key;
            _ = PersistLanguageAsync(value.Key);
        }
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value is null) return;

        AppSettingsService.ApplyTheme(value.Key);
        if (!_loadingSettings && !_syncingAppState)
        {
            _state.ThemeKey = value.Key;
            _ = PersistThemeAsync(value.Key);
        }
    }

    partial void OnAccentColorChanged(Color value)
    {
        UpdateSelectedAccentColor(value);
        var hex = ToHex(value);
        AppSettingsService.ApplyAccentColor(hex);
        if (!_loadingSettings && !_syncingAppState)
        {
            _state.AccentColorHex = hex;
            _ = PersistAccentColorAsync(hex);
        }
    }

    partial void OnLaunchAtStartupChanged(bool value)
    {
        if (_loadingSettings || _syncingAppState || _restoringStartupSetting) return;
        _ = PersistLaunchAtStartupAsync(value);
        _state.LaunchAtStartup = value;
    }

    private async Task PersistLaunchAtStartupAsync(bool enabled)
    {
        await Task.Yield();
        if (_startup.TrySetEnabled(enabled))
        {
            StatusMessage = T(enabled ? "StartupEnabled" : "StartupDisabled");
            return;
        }

        _restoringStartupSetting = true;
        try { LaunchAtStartup = _startup.IsEnabled(); }
        finally { _restoringStartupSetting = false; }

        StatusMessage = T("StartupPermissionRequired");
        _startup.OpenPermissionSettings();
    }

    private async Task PersistThemeAsync(string theme)
    {
        try
        {
            await _settings.SaveAsync(AppSettingsService.ThemeKey, theme).ConfigureAwait(false);
            StatusMessage = T("Ready");
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(GeneralSettingsViewModel), "Failed to persist theme");
            StatusMessage = exception.Message;
        }
    }

    private async Task PersistAccentColorAsync(string hex)
    {
        try
        {
            await _settings.SaveAsync(AppSettingsService.AccentColorKey, hex).ConfigureAwait(false);
            StatusMessage = T("Ready");
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(GeneralSettingsViewModel), "Failed to persist accent");
            StatusMessage = exception.Message;
        }
    }

    private async Task PersistLanguageAsync(string language)
    {
        try
        {
            await _settings.SaveAsync(AppSettingsService.LanguageKey, language).ConfigureAwait(false);
            StatusMessage = T("Ready");
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(GeneralSettingsViewModel), "Failed to persist language");
            StatusMessage = exception.Message;
        }
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private void UpdateSelectedAccentColor(Color color)
    {
        foreach (var option in AccentColorOptions)
        {
            option.IsSelected = option.Color == color;
        }
    }

    private void OnAppStatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _syncingAppState = true;
            try
            {
                switch (e.PropertyName)
                {
                    case nameof(AppState.ThemeKey):
                        SelectedTheme = ThemeOptions.FirstOrDefault(item => item.Key == _state.ThemeKey);
                        break;
                    case nameof(AppState.AccentColorHex):
                        if (AppSettingsService.TryParseColor(_state.AccentColorHex, out var accent))
                            AccentColor = accent;
                        break;
                    case nameof(AppState.LanguageKey):
                        SelectedLanguage = LanguageOptions.FirstOrDefault(item => item.Key == _state.LanguageKey);
                        break;
                    case nameof(AppState.LaunchAtStartup):
                        LaunchAtStartup = _state.LaunchAtStartup;
                        break;
                }
            }
            finally { _syncingAppState = false; }
        });
    }

    public override void Dispose()
    {
        _state.PropertyChanged -= OnAppStatePropertyChanged;
        base.Dispose();
    }
}
