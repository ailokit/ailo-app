using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ailo.Localization;
using Ailo.Logging;
using Ailo.Services;

namespace Ailo.ViewModels;

public sealed partial class ShortcutSettingsViewModel : SettingsViewModelBase
{
    private readonly AppSettingsService _settings;
    private readonly IGlobalHotKeyService? _globalHotKeys;
    private readonly IConfirmationService? _confirmation;
    private readonly AppState _state;
    private bool _loadingSettings;
    private bool _syncingAppState;

    public ShortcutSettingsViewModel(
        AppSettingsService settings,
        IGlobalHotKeyService? globalHotKeys,
        IConfirmationService? confirmation,
        AppState state,
        LocalizationService localization)
        : base(localization)
    {
        _settings = settings;
        _globalHotKeys = globalHotKeys;
        _confirmation = confirmation;
        _state = state;
        _state.PropertyChanged += OnAppStatePropertyChanged;
    }

    [ObservableProperty]
    private string _chatShortcut = AppSettingsService.DefaultChatShortcut;

    [ObservableProperty]
    private string _newChatWindowShortcut = AppSettingsService.DefaultNewChatWindowShortcut;

    public void LoadFromState()
    {
        _loadingSettings = true;
        try
        {
            ChatShortcut = _state.ChatShortcut;
            NewChatWindowShortcut = _state.NewChatWindowShortcut;
        }
        finally { _loadingSettings = false; }
    }

    [RelayCommand]
    private async Task ClearShortcutAsync()
    {
        if (_confirmation is not null && !await _confirmation.ConfirmDeleteAsync(T("ChatShortcut")))
            return;

        ChatShortcut = string.Empty;
        await _settings.SaveAsync(AppSettingsService.ChatShortcutKey, string.Empty).ConfigureAwait(false);
        UpdateGlobalShortcuts();
        StatusMessage = T("ShortcutCleared");
    }

    [RelayCommand]
    private async Task ClearNewChatWindowShortcutAsync()
    {
        if (_confirmation is not null && !await _confirmation.ConfirmDeleteAsync(T("NewChatWindowShortcut")))
            return;

        NewChatWindowShortcut = string.Empty;
        await _settings.SaveAsync(AppSettingsService.NewChatWindowShortcutKey, string.Empty).ConfigureAwait(false);
        UpdateGlobalShortcuts();
        StatusMessage = T("ShortcutCleared");
    }

    partial void OnChatShortcutChanged(string value)
    {
        if (_loadingSettings || _syncingAppState || !ShortcutFormatter.TryParse(value, out _))
            return;

        UpdateGlobalShortcuts();
        _ = PersistShortcutAsync(value);
        _state.ChatShortcut = value;
    }

    partial void OnNewChatWindowShortcutChanged(string value)
    {
        if (_loadingSettings || _syncingAppState || !IsShortcutOrEmpty(value))
            return;

        UpdateGlobalShortcuts();
        _ = PersistNewChatWindowShortcutAsync(value);
        _state.NewChatWindowShortcut = value;
    }

    private void UpdateGlobalShortcuts() => _globalHotKeys?.UpdateShortcuts(ChatShortcut, NewChatWindowShortcut);

    private static bool IsShortcutOrEmpty(string shortcut) =>
        string.IsNullOrEmpty(shortcut) || ShortcutFormatter.TryParse(shortcut, out _);

    private async Task PersistShortcutAsync(string shortcut)
    {
        try
        {
            await _settings.SaveAsync(AppSettingsService.ChatShortcutKey, shortcut).ConfigureAwait(false);
            StatusMessage = T("Ready");
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ShortcutSettingsViewModel), "Failed to persist shortcut");
            StatusMessage = exception.Message;
        }
    }

    private async Task PersistNewChatWindowShortcutAsync(string shortcut)
    {
        try
        {
            await _settings.SaveAsync(AppSettingsService.NewChatWindowShortcutKey, shortcut).ConfigureAwait(false);
            StatusMessage = T("Ready");
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ShortcutSettingsViewModel), "Failed to persist shortcut");
            StatusMessage = exception.Message;
        }
    }

    private void OnAppStatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _syncingAppState = true;
        try
        {
            switch (e.PropertyName)
            {
                case nameof(AppState.ChatShortcut):
                    ChatShortcut = _state.ChatShortcut;
                    break;
                case nameof(AppState.NewChatWindowShortcut):
                    NewChatWindowShortcut = _state.NewChatWindowShortcut;
                    break;
            }
        }
        finally { _syncingAppState = false; }
    }

    public override void Dispose()
    {
        _state.PropertyChanged -= OnAppStatePropertyChanged;
        base.Dispose();
    }
}
