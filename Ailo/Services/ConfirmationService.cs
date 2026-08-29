using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Ailo.Localization;
using Ailo.Views;

namespace Ailo.Services;

public sealed class ConfirmationService(LocalizationService localization) : IConfirmationService
{
    public async Task<bool> ConfirmDeleteAsync(string itemName)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return false;
        }

        var owner = desktop.Windows.FirstOrDefault(window => window.IsActive && window.IsVisible)
            ?? desktop.Windows.LastOrDefault(window => window.IsVisible)
            ?? desktop.MainWindow;
        if (owner is null || !owner.IsVisible)
        {
            return false;
        }

        var dialog = new ConfirmationDialog(
            localization["ConfirmDeleteTitle"],
            string.Format(localization["ConfirmDeleteMessage"], itemName),
            localization["Cancel"],
            localization["Delete"]);
        return await dialog.ShowDialog<bool>(owner);
    }
}
