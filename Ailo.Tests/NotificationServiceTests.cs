using Ailo.Services;

namespace Ailo.Tests;

public sealed class NotificationServiceTests
{
    [Theory]
    [InlineData(NotificationType.Native)]
    [InlineData(NotificationType.TopmostWindow)]
    public async Task RoutesNotificationToTheSelectedDeliveryService(NotificationType type)
    {
        var native = new FakePlatformNotificationService();
        var topmost = new FakeTopmostNotificationService();
        var service = new NotificationService(native, topmost);

        await service.ShowAsync(type, "Title", "Body", "Subtitle");

        if (type == NotificationType.Native)
        {
            Assert.Equal(("Title", "Body", "Subtitle"), native.LastNotification);
            Assert.Null(topmost.LastNotification);
            return;
        }

        Assert.Equal(("Title", "Body", "Subtitle"), topmost.LastNotification);
        Assert.Null(native.LastNotification);
    }

    private sealed class FakePlatformNotificationService : IPlatformNotificationService
    {
        public (string Title, string Body, string? Subtitle)? LastNotification { get; private set; }

        public Task ShowAsync(string title, string body, string? subtitle = null, CancellationToken cancellationToken = default)
        {
            LastNotification = (title, body, subtitle);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTopmostNotificationService : ITopmostNotificationService
    {
        public (string Title, string Body, string? Subtitle)? LastNotification { get; private set; }

        public Task ShowAsync(string title, string body, string? subtitle = null, CancellationToken cancellationToken = default)
        {
            LastNotification = (title, body, subtitle);
            return Task.CompletedTask;
        }
    }
}
