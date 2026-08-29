namespace Ailo.Services;

/// <summary>Delivers a notification using the selected user-visible behavior.</summary>
public interface INotificationService
{
    Task ShowAsync(
        NotificationType type,
        string title,
        string body,
        string? subtitle = null,
        CancellationToken cancellationToken = default);
}
