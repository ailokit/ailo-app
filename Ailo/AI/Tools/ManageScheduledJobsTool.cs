using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ailo.Jobs;

namespace Ailo.AI.Tools;

/// <summary>Agent-facing tools for inspecting and managing persisted recurring jobs.</summary>
public sealed class ManageScheduledJobsTool(CronJobScheduler scheduler)
{
    [Description("Lists all persistent recurring jobs, including disabled jobs. Use the returned id when updating or deleting a job.")]
    public async Task<string> ListScheduledJobsAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await scheduler.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(jobs.Select(ToDto).ToArray(), ScheduledJobsJsonSerializerContext.Default.ScheduledJobDtoArray);
    }

    [Description("Updates a persistent recurring job by id. Omitted fields keep their current values. parametersJson must be a valid JSON object or value accepted by the job type.")]
    public async Task<string> UpdateScheduledJobAsync(
        [Description("The numeric id returned by list_scheduled_jobs or schedule_notification.")] int jobId,
        [Description("Optional five- or six-field Cron expression in local time. For example, '0 9 * * 1-5'.")] string? cronExpression = null,
        [Description("Optional JSON parameters for the job. Notification jobs use {\"title\":\"...\",\"body\":\"...\",\"subtitle\":null}; agent jobs use {\"prompt\":\"...\",\"workingDirectory\":\"/absolute/path\"}, or omit workingDirectory to use the configured default workspace when the job runs.")] string? parametersJson = null,
        [Description("Optional enabled state. Set false to pause the job, or true to resume it.")] bool? isEnabled = null,
        [Description("Optional one-time state. Set true to delete the job automatically after execution, or false to keep it recurring.")] bool? isOneTime = null,
        CancellationToken cancellationToken = default)
    {
        var job = await scheduler.UpdateAsync(jobId, cronExpression, parametersJson, isEnabled, cancellationToken, isOneTime).ConfigureAwait(false);
        if (job is null) return $"Scheduled job {jobId} was not found.";
        return $"Scheduled job updated. ID: {job.Id}; Cron: {job.CronExpression}; Status: {(job.IsEnabled ? "enabled" : "disabled")}; Next run: {job.NextRunAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} (local time).";
    }

    [Description("Permanently deletes a persistent recurring job by id. Ask for confirmation when the user has not clearly requested deletion.")]
    public async Task<string> DeleteScheduledJobAsync(
        [Description("The numeric id returned by list_scheduled_jobs or schedule_notification.")] int jobId,
        CancellationToken cancellationToken = default)
    {
        var deleted = await scheduler.DeleteAsync(jobId, cancellationToken).ConfigureAwait(false);
        return deleted ? $"Scheduled job {jobId} deleted." : $"Scheduled job {jobId} was not found.";
    }

    private static ScheduledJobDto ToDto(CronJob job) => new(
        job.Id,
        job.JobType,
        job.CronExpression,
        job.ParametersJson,
        job.IsEnabled,
        job.IsOneTime,
        job.LastRunAtUtc?.ToLocalTime(),
        job.NextRunAtUtc.ToLocalTime(),
        job.CreatedAtUtc.ToLocalTime(),
        job.UpdatedAtUtc.ToLocalTime());

}

internal sealed record ScheduledJobDto(
    int Id,
    string JobType,
    string CronExpression,
    string ParametersJson,
    bool IsEnabled,
    bool IsOneTime,
    DateTimeOffset? LastRunAt,
    DateTimeOffset NextRunAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

[JsonSerializable(typeof(ScheduledJobDto[]))]
internal sealed partial class ScheduledJobsJsonSerializerContext : JsonSerializerContext;
