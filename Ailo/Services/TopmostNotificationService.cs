using Avalonia.Threading;
using Ailo.Views;

namespace Ailo.Services;

/// <summary>Creates user-dismissible, always-on-top notification windows on the Avalonia UI thread.</summary>
public sealed class TopmostNotificationService : ITopmostNotificationService
{
    public Task ShowAsync(string title, string body, string? subtitle = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        cancellationToken.ThrowIfCancellationRequested();
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = new TopmostNotificationWindow(title, body, subtitle);
            window.Show();
            window.Activate();
        }).GetTask();
    }
}
