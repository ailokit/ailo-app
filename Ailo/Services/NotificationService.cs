namespace Ailo.Services;

/// <summary>Routes notifications to either the operating system or Ailo's topmost window.</summary>
public sealed class NotificationService(
    IPlatformNotificationService platformNotifications,
    ITopmostNotificationService topmostNotifications) : INotificationService
{
    public Task ShowAsync(
        NotificationType type,
        string title,
        string body,
        string? subtitle = null,
        CancellationToken cancellationToken = default) => type switch
    {
        NotificationType.Native => platformNotifications.ShowAsync(title, body, subtitle, cancellationToken),
        NotificationType.TopmostWindow => topmostNotifications.ShowAsync(title, body, subtitle, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported notification type.")
    };
}
