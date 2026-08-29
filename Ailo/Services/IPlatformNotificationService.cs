namespace Ailo.Services;

public interface IPlatformNotificationService
{
    Task ShowAsync(string title, string body, string? subtitle = null, CancellationToken cancellationToken = default);
}
