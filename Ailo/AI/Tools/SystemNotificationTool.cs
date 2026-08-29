using System.ComponentModel;
using Ailo.Services;

namespace Ailo.AI.Tools;

/// <summary>Agent-facing tool for sending an immediate native desktop notification.</summary>
public sealed class SystemNotificationTool(INotificationService notifications)
{
    [Description("Sends an immediate notification. notificationType selects Native system notifications or a user-dismissible TopmostWindow, whose body supports Markdown rendering. Use this when the user asks to be notified now, or when a background task needs to report a meaningful completion, failure, or required attention.")]
    public async Task<string> ShowNotificationAsync(
        [Description("Notification title, up to 200 characters.")] string title,
        [Description("Notification body, up to 4000 characters. TopmostWindow renders this body as Markdown.")] string body,
        [Description("Optional notification subtitle, up to 200 characters.")] string? subtitle = null,
        [Description("Delivery behavior: Native for an operating-system notification, or TopmostWindow to show an always-on-top Ailo window with a Markdown-rendered body.")] NotificationType notificationType = NotificationType.Native,
        CancellationToken cancellationToken = default)
    {
        Validate(title, body, subtitle);
        await notifications.ShowAsync(notificationType, title, body, subtitle, cancellationToken).ConfigureAwait(false);
        return $"Notification sent: {title}";
    }

    private static void Validate(string title, string body, string? subtitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        if (title.Length > 200) throw new ArgumentException("Notification title cannot exceed 200 characters.", nameof(title));
        if (body.Length > 4000) throw new ArgumentException("Notification body cannot exceed 4000 characters.", nameof(body));
        if (subtitle?.Length > 200) throw new ArgumentException("Notification subtitle cannot exceed 200 characters.", nameof(subtitle));
    }
}
