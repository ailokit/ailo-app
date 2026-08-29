using Ailo.Data;
using Ailo.Jobs;
using Ailo.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ailo.Tests;

public sealed class NotificationJobTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task PersistsAndExecutesNotificationParametersThroughTheJob()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        var notifications = new FakeNotificationService();
        var notificationJob = new NotificationJob(notifications);
        using var scheduler = new CronJobScheduler(
            new CronJobRepository(database),
            [notificationJob],
            NullLogger<CronJobScheduler>.Instance);

        var job = await NotificationJob.ScheduleAsync(scheduler, "0 9 * * 1-5", "Title", "Body", "Subtitle");
        await notificationJob.ExecuteAsync(job, CancellationToken.None);

        Assert.Equal((NotificationType.Native, "Title", "Body", "Subtitle"), notifications.LastNotification);
    }

    [Fact]
    public async Task ExecutesLegacyNotificationParametersAsNativeNotification()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        var notifications = new FakeNotificationService();
        var notificationJob = new NotificationJob(notifications);
        using var scheduler = new CronJobScheduler(
            new CronJobRepository(database),
            [notificationJob],
            NullLogger<CronJobScheduler>.Instance);

        var job = await scheduler.ScheduleAsync(NotificationJob.Type, "0 9 * * 1-5", "{\"Title\":\"Legacy\",\"Body\":\"Body\",\"Subtitle\":null}");
        await notificationJob.ExecuteAsync(job, CancellationToken.None);

        Assert.Equal((NotificationType.Native, "Legacy", "Body", null), notifications.LastNotification);
    }

    [Fact]
    public async Task PersistsTopmostWindowDeliveryType()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        var notifications = new FakeNotificationService();
        var notificationJob = new NotificationJob(notifications);
        using var scheduler = new CronJobScheduler(
            new CronJobRepository(database),
            [notificationJob],
            NullLogger<CronJobScheduler>.Instance);

        var job = await NotificationJob.ScheduleAsync(
            scheduler,
            "0 9 * * 1-5",
            "Title",
            "Body",
            notificationType: NotificationType.TopmostWindow);
        await notificationJob.ExecuteAsync(job, CancellationToken.None);

        Assert.Equal((NotificationType.TopmostWindow, "Title", "Body", null), notifications.LastNotification);
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public (NotificationType Type, string Title, string Body, string? Subtitle)? LastNotification { get; private set; }

        public Task ShowAsync(NotificationType type, string title, string body, string? subtitle = null, CancellationToken cancellationToken = default)
        {
            LastNotification = (type, title, body, subtitle);
            return Task.CompletedTask;
        }
    }
}
