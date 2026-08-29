using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using Ailo.AI.Tools;
using Ailo.Localization;
using Ailo.Logging;
using Ailo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Ailo.ViewModels;

/// <summary>
/// Process-wide state shared by chat and settings windows.
/// Persistent settings are still stored by AppSettingsService; this object is
/// the live source of truth while the application is running.
/// </summary>
public sealed partial class AppState : ObservableObject
{
    private readonly LazyServiceProvider _lazyServiceProvider;
    private readonly LocalizationService _localization;
    private readonly ShellToolConfiguration? _shellToolConfiguration;
    private Func<CancellationToken, Task<AppUpdateInfo?>>? _checkForUpdates;

    public AppState(
        LazyServiceProvider lazyServiceProvider,
        LocalizationService? localization = null,
        ShellToolConfiguration? shellToolConfiguration = null)
    {
        _lazyServiceProvider = lazyServiceProvider;
        _localization = localization ?? new LocalizationService();
        _shellToolConfiguration = shellToolConfiguration;
        _localization.PropertyChanged += OnLocalizationPropertyChanged;
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        Tools.CollectionChanged += OnToolsCollectionChanged;
        EnsureToolsInitialized();
    }

    private AppSettingsService AppSettingsService => _lazyServiceProvider.GetRequiredService<AppSettingsService>();
    
    public IAsyncRelayCommand CheckForUpdatesCommand { get; }

    /// <summary>All known tools and their process-wide availability.</summary>
    public ObservableCollection<ToolAvailability> Tools { get; } = [];

    /// <summary>Alias used by consumers that treat each entry as a tool state.</summary>
    public ObservableCollection<ToolAvailability> ToolStates => Tools;

    public IReadOnlyList<ToolAvailability> AvailableTools => Tools.Where(static tool => tool.IsEnabled).ToArray();

    public IReadOnlySet<string> EnabledToolNames => Tools.Where(static tool => tool.IsEnabled)
        .Select(static tool => tool.Name)
        .ToHashSet(StringComparer.Ordinal);

    public ShellToolConfiguration? ShellToolConfiguration => _shellToolConfiguration;

    [ObservableProperty] private string _themeKey = AppSettingsService.DefaultTheme;

    [ObservableProperty] private string _accentColorHex = AppSettingsService.DefaultAccentColor;

    [ObservableProperty] private string _languageKey = AppSettingsService.DefaultLanguage;

    [ObservableProperty] private string _chatShortcut = AppSettingsService.DefaultChatShortcut;

    [ObservableProperty] private string _newChatWindowShortcut = AppSettingsService.DefaultNewChatWindowShortcut;

    [ObservableProperty] private bool _launchAtStartup;

    [ObservableProperty] private UpdateProgress _updateProgress = new(UpdateState.Idle);

    [ObservableProperty] private string _currentVersion = "0.0.0";

    public bool SettingsLoaded { get; set; }

    public bool CanCheckForUpdates => UpdateProgress.State is not (UpdateState.Checking or UpdateState.Downloading);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (_shellToolConfiguration is not null)
            await _shellToolConfiguration.LoadAsync(cancellationToken).ConfigureAwait(false);
        await LoadAiStateAsync(cancellationToken).ConfigureAwait(false);
    }

    internal void ConfigureUpdateChecker(Func<CancellationToken, Task<AppUpdateInfo?>> checkForUpdates)
    {
        _checkForUpdates = checkForUpdates;
    }

    partial void OnUpdateProgressChanged(UpdateProgress value)
    {
        OnPropertyChanged(nameof(CanCheckForUpdates));
    }

    private async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!SettingsLoaded)
        {
            ThemeKey = await AppSettingsService.GetAsync(AppSettingsService.ThemeKey, cancellationToken)
                                  .ConfigureAwait(false)
                              ?? AppSettingsService.DefaultTheme;
            LanguageKey = await AppSettingsService.GetAsync(AppSettingsService.LanguageKey, cancellationToken)
                                     .ConfigureAwait(false)
                                 ?? AppSettingsService.DefaultLanguage;
            ChatShortcut = await AppSettingsService.GetAsync(AppSettingsService.ChatShortcutKey, cancellationToken)
                                      .ConfigureAwait(false)
                                  ?? AppSettingsService.DefaultChatShortcut;
            NewChatWindowShortcut = await AppSettingsService
                                               .GetAsync(AppSettingsService.NewChatWindowShortcutKey, cancellationToken)
                                               .ConfigureAwait(false)
                                           ?? AppSettingsService.DefaultNewChatWindowShortcut;
            await LoadToolStateAsync(cancellationToken).ConfigureAwait(false);
            SettingsLoaded = true;
        }
    }

    /// <summary>Ensures consumers can bind the tool list even before startup loading completes.</summary>
    public void EnsureToolsInitialized()
    {
        if (Tools.Count > 0) return;
        foreach (var definition in ChatToolCatalog.All)
        {
            Tools.Add(new ToolAvailability(definition.Name, definition.DisplayNameKey, definition.DescriptionKey, definition.CategoryKey, _localization));
        }
    }

    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var tool in Tools)
            tool.RefreshLocalization(_localization);
    }

    private async Task LoadToolStateAsync(CancellationToken cancellationToken)
    {
        EnsureToolsInitialized();
        var raw = await AppSettingsService.GetAsync(AppSettingsService.EnabledToolsKey, cancellationToken)
            .ConfigureAwait(false);
        HashSet<string>? enabled = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                enabled = JsonSerializer.Deserialize(raw, Ailo.AI.AiloJsonSerializerContext.Default.StringArray)
                    ?.ToHashSet(StringComparer.Ordinal);
            }
            catch (JsonException exception)
            {
                ExceptionLogger.Log(exception, nameof(AppState), "Enabled tool settings are invalid");
            }
        }

        _loadingTools = true;
        try
        {
            foreach (var tool in Tools)
                tool.IsEnabled = enabled is null || enabled.Contains(tool.Name);
        }
        finally
        {
            _loadingTools = false;
        }
        OnPropertyChanged(nameof(AvailableTools));
        OnPropertyChanged(nameof(EnabledToolNames));
    }

    private bool _loadingTools;

    private void OnToolsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (ToolAvailability tool in e.NewItems)
                tool.PropertyChanged += OnToolPropertyChanged;
        }
        if (e.OldItems is not null)
        {
            foreach (ToolAvailability tool in e.OldItems)
                tool.PropertyChanged -= OnToolPropertyChanged;
        }
        OnPropertyChanged(nameof(AvailableTools));
        OnPropertyChanged(nameof(EnabledToolNames));
    }

    private void OnToolPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ToolAvailability.IsEnabled)) return;
        OnPropertyChanged(nameof(AvailableTools));
        OnPropertyChanged(nameof(EnabledToolNames));
        if (!_loadingTools) _ = PersistToolStateAsync();
    }

    private async Task PersistToolStateAsync()
    {
        try
        {
            var value = JsonSerializer.Serialize(Tools.Where(static tool => tool.IsEnabled)
                .Select(static tool => tool.Name).ToArray(), Ailo.AI.AiloJsonSerializerContext.Default.StringArray);
            await AppSettingsService.SaveAsync(AppSettingsService.EnabledToolsKey, value).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(AppState), "Failed to persist enabled tools");
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_checkForUpdates is not null)
        {
            try
            {
                await _checkForUpdates(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                ExceptionLogger.Log(exception, nameof(AppState), "Update command failed");
                // UpdateService publishes the failure through UpdateProgress.
            }
        }
    }
}
