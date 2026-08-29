using System.Collections.ObjectModel;
using Ailo.AI.Tools;
using Ailo.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ailo.ViewModels;

public sealed class ToolSettingsViewModel : SettingsViewModelBase
{
    private readonly ShellToolConfiguration? _shellToolConfiguration;

    public ToolSettingsViewModel(
        AppState appState,
        LocalizationService localization,
        ShellToolConfiguration? shellToolConfiguration = null)
        : base(localization)
    {
        AppState = appState;
        _shellToolConfiguration = shellToolConfiguration ?? appState.ShellToolConfiguration;
        AppState.EnsureToolsInitialized();
        foreach (var group in AppState.Tools.GroupBy(tool => tool.CategoryKey))
            ToolCategories.Add(new ToolCategory(group.Key, localization, group));
        localization.PropertyChanged += OnLocalizationPropertyChanged;
        ShellToolOptions = _shellToolConfiguration is null
            ? []
            :
            [
                new ShellToolOption(_shellToolConfiguration, localization, ShellToolKind.Local, "ShellToolLocal", "ShellToolLocalDescription"),
                new ShellToolOption(_shellToolConfiguration, localization, ShellToolKind.Docker, "ShellToolDocker", "ShellToolDockerDescription")
            ];

        if (_shellToolConfiguration is not null)
            _shellToolConfiguration.PropertyChanged += OnShellToolConfigurationChanged;
    }

    public AppState AppState { get; }

    public ObservableCollection<ShellToolOption> ShellToolOptions { get; } = [];

    public ObservableCollection<ToolCategory> ToolCategories { get; } = [];

    public bool IsShellToolEnabled
    {
        get => _shellToolConfiguration?.IsEnabled ?? false;
        set
        {
            if (_shellToolConfiguration is null || value == IsShellToolEnabled)
                return;

            _shellToolConfiguration.SetEnabled(value);
        }
    }

    public ShellToolOption? SelectedShellToolOption
    {
        get => ShellToolOptions.FirstOrDefault(option => option.Kind == _shellToolConfiguration?.SelectedTool);
        set
        {
            if (value is null || _shellToolConfiguration is null)
                return;

            if (!value.IsAvailable)
            {
                OnPropertyChanged(nameof(SelectedShellToolOption));
                return;
            }

            _shellToolConfiguration.TrySelect(value.Kind);
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        AppState.EnsureToolsInitialized();
        if (_shellToolConfiguration is not null)
            await _shellToolConfiguration.LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        Localization.PropertyChanged -= OnLocalizationPropertyChanged;
        foreach (var category in ToolCategories)
            category.Dispose();
        if (_shellToolConfiguration is not null)
            _shellToolConfiguration.PropertyChanged -= OnShellToolConfigurationChanged;
        foreach (var option in ShellToolOptions)
            option.Dispose();
        base.Dispose();
    }

    private void OnLocalizationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        foreach (var category in ToolCategories)
            category.RefreshLocalization(Localization);
    }

    private void OnShellToolConfigurationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellToolConfiguration.IsEnabled))
            OnPropertyChanged(nameof(IsShellToolEnabled));

        if (e.PropertyName == nameof(ShellToolConfiguration.SelectedTool))
            OnPropertyChanged(nameof(SelectedShellToolOption));
    }
}

public sealed partial class ToolCategory : ObservableObject, IDisposable
{
    private readonly string _localizationKey;
    private bool _updatingSelection;

    public ToolCategory(string localizationKey, LocalizationService localization, IEnumerable<ToolAvailability> tools)
    {
        _localizationKey = localizationKey;
        DisplayName = localization[localizationKey];
        foreach (var tool in tools)
        {
            Tools.Add(tool);
            tool.PropertyChanged += OnToolPropertyChanged;
        }
    }

    public ObservableCollection<ToolAvailability> Tools { get; } = [];

    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private bool _isExpanded = true;

    public bool? IsChecked
    {
        get
        {
            if (Tools.Count == 0) return false;
            var enabled = Tools.Count(tool => tool.IsEnabled);
            return enabled == 0 ? false : enabled == Tools.Count ? true : null;
        }
        set
        {
            if (value is null || _updatingSelection) return;
            _updatingSelection = true;
            try
            {
                foreach (var tool in Tools)
                    tool.IsEnabled = value.Value;
            }
            finally
            {
                _updatingSelection = false;
            }
            OnPropertyChanged(nameof(IsChecked));
        }
    }

    public void RefreshLocalization(LocalizationService localization) => DisplayName = localization[_localizationKey];

    public void Dispose()
    {
        foreach (var tool in Tools)
            tool.PropertyChanged -= OnToolPropertyChanged;
    }

    private void OnToolPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolAvailability.IsEnabled))
            OnPropertyChanged(nameof(IsChecked));
    }
}
