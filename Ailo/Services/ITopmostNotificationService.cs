namespace Ailo.Services;

/// <summary>Displays an application notification in an always-on-top window.</summary>
public interface ITopmostNotificationService
{
    Task ShowAsync(string title, string body, string? subtitle = null, CancellationToken cancellationToken = default);
}
