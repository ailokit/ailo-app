using System.ComponentModel;
using Ailo.Jobs;
using Ailo.Services;

namespace Ailo.AI.Tools;

/// <summary>Agent-facing tool for creating persistent recurring notifications.</summary>
public sealed class ScheduleNotificationTool(CronJobScheduler scheduler)
{
    [Description("Creates a persistent recurring notification in the user's local time zone. notificationType selects Native system notifications or a user-dismissible TopmostWindow, whose body supports Markdown rendering. Use five cron fields (minute hour day-of-month month day-of-week), or six fields with seconds first for sub-minute schedules. The schedule remains active after the app restarts.")]
    public async Task<string> ScheduleNotificationAsync(
        [Description("Cron expression in local time. Use '0 9 * * 1-5' for weekdays at 09:00, or '*/10 * * * * *' to run every 10 seconds (six fields, seconds first).")] string cronExpression,
        [Description("Notification title.")] string title,
        [Description("Notification body/content. TopmostWindow renders this body as Markdown.")] string body,
        [Description("Optional notification subtitle.")] string? subtitle = null,
        [Description("Delivery behavior: Native for an operating-system notification, or TopmostWindow to show an always-on-top Ailo window with a Markdown-rendered body.")] NotificationType notificationType = NotificationType.Native,
        CancellationToken cancellationToken = default)
    {
        var job = await NotificationJob.ScheduleAsync(scheduler, cronExpression, title, body, subtitle, notificationType, cancellationToken).ConfigureAwait(false);
        var nextRun = job.NextRunAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        return $"Scheduled notification created. ID: {job.Id}; Cron: {job.CronExpression}; Next run: {nextRun} (local time).";
    }
}
