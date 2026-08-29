using System.ComponentModel;
using Ailo.Jobs;

namespace Ailo.AI.Tools;

/// <summary>Agent-facing tool for creating persistent, Docker-confined recurring agent jobs.</summary>
public sealed class ScheduleAgentJobTool(CronJobScheduler scheduler, ShellToolConfiguration shellToolConfiguration)
{
    [Description("Creates a persistent recurring agent job. The task runs in a fresh agent invocation on every occurrence, uses the enabled default AI provider, and remains active after restart. Its shell is Docker-only and confined to workingDirectory. The workspace file and directory tools are not available to the scheduled agent.")]
    public async Task<string> ScheduleAgentJobAsync(
        [Description("Five-field local-time Cron expression, such as '0 9 * * 1-5', or a six-field expression with seconds first.")] string cronExpression,
        [Description("The task prompt to run on every schedule occurrence.")] string prompt,
        [Description("Existing absolute working directory to mount as the scheduled agent's only local workspace. It may contain scripts used by the task.")] string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!shellToolConfiguration.IsEnabled)
        {
            throw new InvalidOperationException("Shell execution is disabled in the tool settings.");
        }

        if (!shellToolConfiguration.IsDockerShellAvailable || string.IsNullOrWhiteSpace(shellToolConfiguration.ContainerRuntimeBinary))
        {
            throw new InvalidOperationException("A running Docker or Podman OCI runtime is required to create a scheduled agent job.");
        }

        var job = await AgentJob.ScheduleAsync(scheduler, cronExpression, prompt, workingDirectory, cancellationToken).ConfigureAwait(false);
        var nextRun = job.NextRunAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        return $"Scheduled agent job created. ID: {job.Id}; Cron: {job.CronExpression}; Working directory: {workingDirectory}; Next run: {nextRun} (local time).";
    }
}
