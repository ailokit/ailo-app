using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Ailo.ViewModels;
using Ailo.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Ailo.Services;

/// <summary>
/// Coordinates top-level windows. Each window is resolved from a dedicated
/// <see cref="IServiceScope"/> so that the window, its ViewModel, and all
/// transient dependencies are scoped to the window's lifetime. The scope is
/// disposed when the window closes, releasing all transient resources.
/// </summary>
/// <remarks>
/// ViewModel initialization and data loading are handled internally by each
/// window in <see cref="Window.OnOpened"/>. This service only creates windows
/// and passes pre-show parameters (conversation id, page key).
/// </remarks>
public sealed class WindowNavigationService(IServiceScopeFactory scopeFactory) : IWindowNavigationService
{
    private SettingsWindow? _settingsWindow;

    public void ShowSettings()
    {
        ShowSettings(SettingsWindowViewModel.GeneralSettingsPage);
    }

    public void ShowSkillSettings()
    {
        ShowSettings(SettingsWindowViewModel.SkillsSettingsPage);
    }

    private void ShowSettings(string pageKey)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.NavigateTo(pageKey);
            _settingsWindow.Activate();
            _settingsWindow.Focus();
            return;
        }

        var scope = scopeFactory.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<SettingsWindow>();
        window.SetScope(scope);
        window.PendingPageKey = pageKey;

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(window, _settingsWindow))
                _settingsWindow = null;
        };

        _settingsWindow = window;
        window.Show();
        window.Activate();
        window.Focus();
    }
}
