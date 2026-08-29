using Avalonia.Controls;
using Avalonia.Input;
using Ailo.Services;
using Ailo.ViewModels;

namespace Ailo.Views.Settings;

public partial class ShortcutSettingsView : UserControl
{
    public ShortcutSettingsView() => InitializeComponent();

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is ShortcutSettingsViewModel viewModel && ShortcutFormatter.TryFormat(e.Key, e.KeyModifiers, out var shortcut))
        {
            viewModel.ChatShortcut = shortcut;
            e.Handled = true;
        }
    }

    private void OnNewChatWindowShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is ShortcutSettingsViewModel viewModel && ShortcutFormatter.TryFormat(e.Key, e.KeyModifiers, out var shortcut))
        {
            viewModel.NewChatWindowShortcut = shortcut;
            e.Handled = true;
        }
    }
}
