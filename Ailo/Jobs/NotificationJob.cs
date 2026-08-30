using System.Text.Json;
using System.Text.Json.Serialization;
using Ailo.Services;

namespace Ailo.Jobs;

/// <summary>Runs and creates persisted notifications with a selected delivery behavior.</summary>
public sealed class NotificationJob(INotificationService notifications) : ICronJobHandler
{
    public const string Type = "native_notification";

    public string JobType => Type;

    public static Task<CronJob> ScheduleAsync(
        CronJobScheduler scheduler,
        string cronExpression,
        string title,
        string body,
        string? subtitle = null,
        NotificationType notificationType = NotificationType.Native,
        CancellationToken cancellationToken = default,
        bool isOneTime = false)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ValidateParameters(title, body, subtitle);
        var parameters = new NotificationJobParameters(title, body, subtitle, notificationType);
        var parametersJson = JsonSerializer.Serialize(parameters, NotificationJobJsonContext.Default.NotificationJobParameters);
        return scheduler.ScheduleAsync(Type, cronExpression, parametersJson, cancellationToken, isOneTime);
    }

    public async Task ExecuteAsync(CronJob job, CancellationToken cancellationToken)
    {
        var parameters = JsonSerializer.Deserialize(job.ParametersJson, NotificationJobJsonContext.Default.NotificationJobParameters)
            ?? throw new JsonException("Notification job parameters cannot be null.");
        await notifications.ShowAsync(parameters.NotificationType, parameters.Title, parameters.Body, parameters.Subtitle, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateParameters(string title, string body, string? subtitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        if (title.Length > 200)
        {
            throw new ArgumentException("Notification title cannot exceed 200 characters.", nameof(title));
        }

        if (body.Length > 4000)
        {
            throw new ArgumentException("Notification body cannot exceed 4000 characters.", nameof(body));
        }

        if (subtitle?.Length > 200)
        {
            throw new ArgumentException("Notification subtitle cannot exceed 200 characters.", nameof(subtitle));
        }
    }
}

internal sealed record NotificationJobParameters(
    string Title,
    string Body,
    string? Subtitle,
    NotificationType NotificationType = NotificationType.Native);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(NotificationJobParameters))]
internal sealed partial class NotificationJobJsonContext : JsonSerializerContext;
