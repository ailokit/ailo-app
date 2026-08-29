namespace Ailo.Jobs;

/// <summary>Persisted definition and checkpoint for a recurring Cron job.</summary>
public sealed record CronJob(
    int Id,
    string JobType,
    string CronExpression,
    string ParametersJson,
    bool IsEnabled,
    DateTimeOffset? LastRunAtUtc,
    DateTimeOffset NextRunAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
