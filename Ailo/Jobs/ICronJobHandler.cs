namespace Ailo.Jobs;

/// <summary>Executes one persisted Cron job type.</summary>
public interface ICronJobHandler
{
    string JobType { get; }

    Task ExecuteAsync(CronJob job, CancellationToken cancellationToken);
}
