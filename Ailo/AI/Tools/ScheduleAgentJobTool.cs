using System.ComponentModel;
using Ailo.Jobs;

namespace Ailo.AI.Tools;

/// <summary>Agent-facing tool for creating persistent, locally confined recurring agent jobs.</summary>
public sealed class ScheduleAgentJobTool(
    CronJobScheduler scheduler,
    ShellToolConfiguration shellToolConfiguration)
{
    [Description("Creates a persistent recurring agent job. The task runs in a fresh agent invocation on every occurrence, uses the enabled default AI provider, and remains active after restart. Its shell runs locally and is confined to workingDirectory. If workingDirectory is omitted, the configured default workspace is selected when the job runs. Set isOneTime to true to delete the job automatically after its execution; it defaults to false. The workspace file and directory tools are not available to the scheduled agent.")]
    public async Task<string> ScheduleAgentJobAsync(
        [Description("Five-field local-time Cron expression, such as '0 9 * * 1-5', or a six-field expression with seconds first.")] string cronExpression,
        [Description("The task prompt to run on every schedule occurrence.")] string prompt,
        [Description("Optional absolute working directory for the scheduled agent's local shell. If omitted, the configured default workspace is selected when the job runs. It may contain scripts used by the task.")] string? workingDirectory = null,
        [Description("Whether to run only once and delete the job after execution. Defaults to false.")] bool isOneTime = false,
        CancellationToken cancellationToken = default)
    {
        if (!shellToolConfiguration.IsEnabled)
        {
            throw new InvalidOperationException("Shell execution is disabled in the tool settings.");
        }

        var job = await AgentJob.ScheduleAsync(scheduler, cronExpression, prompt, workingDirectory, cancellationToken, isOneTime).ConfigureAwait(false);
        var nextRun = job.NextRunAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        var directoryDescription = string.IsNullOrWhiteSpace(workingDirectory)
            ? "configured default workspace"
            : workingDirectory;
        return $"Scheduled agent job created. ID: {job.Id}; Cron: {job.CronExpression}; Working directory: {directoryDescription}; Next run: {nextRun} (local time).";
    }
}
