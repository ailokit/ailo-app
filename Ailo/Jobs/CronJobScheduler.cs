using System.Text.Json;
using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ailo.Jobs;

/// <summary>Runs persisted Cron jobs and wakes immediately when a new job is scheduled.</summary>
public sealed class CronJobScheduler : BackgroundService
{
    private static readonly TimeSpan MaxWaitInterval = TimeSpan.FromMinutes(5);
    private readonly CronJobRepository _repository;
    private readonly IReadOnlyDictionary<string, ICronJobHandler> _handlers;
    private readonly ILogger<CronJobScheduler> _logger;
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);

    public CronJobScheduler(
        CronJobRepository repository,
        IEnumerable<ICronJobHandler> handlers,
        ILogger<CronJobScheduler> logger)
    {
        _repository = repository;
        _handlers = handlers.ToDictionary(handler => handler.JobType, StringComparer.Ordinal);
        _logger = logger;
    }

    /// <summary>Persists a five-field or seconds-enabled six-field Cron job with serialized JSON parameters.</summary>
    public async Task<CronJob> ScheduleAsync(
        string jobType,
        string cronExpression,
        string parametersJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobType);
        ArgumentException.ThrowIfNullOrWhiteSpace(parametersJson);
        if (!_handlers.ContainsKey(jobType))
        {
            throw new ArgumentException($"No Cron job handler is registered for '{jobType}'.", nameof(jobType));
        }

        using var _ = JsonDocument.Parse(parametersJson);
        ValidateParameters(jobType, parametersJson);
        var cron = ParseCron(cronExpression);
        var now = DateTimeOffset.UtcNow;
        var nextRun = GetNextRunAtUtc(cron, now);
        var job = new CronJob(
            0,
            jobType,
            cronExpression.Trim(),
            parametersJson,
            true,
            null,
            nextRun,
            now,
            now);

        job = await _repository.CreateAsync(job, cancellationToken).ConfigureAwait(false);
        WakeWorker();
        return job;
    }

    public Task<IReadOnlyList<CronJob>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _repository.GetAllAsync(cancellationToken);

    public Task<CronJob?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _repository.GetByIdAsync(id, cancellationToken);

    /// <summary>Updates a persisted job while preserving its execution history.</summary>
    public async Task<CronJob?> UpdateAsync(
        int id,
        string? cronExpression = null,
        string? parametersJson = null,
        bool? isEnabled = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        var existing = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null) return null;

        var updatedCronExpression = string.IsNullOrWhiteSpace(cronExpression)
            ? existing.CronExpression
            : cronExpression.Trim();
        var updatedParametersJson = string.IsNullOrWhiteSpace(parametersJson)
            ? existing.ParametersJson
            : parametersJson.Trim();

        using var _ = JsonDocument.Parse(updatedParametersJson);
        // Validate only replacement parameters. A job whose formerly valid working directory has
        // since disappeared must still be pausable, reschedulable, or deletable by the user.
        if (!string.IsNullOrWhiteSpace(parametersJson))
        {
            ValidateParameters(existing.JobType, updatedParametersJson);
        }
        var cron = ParseCron(updatedCronExpression);
        var enabled = isEnabled ?? existing.IsEnabled;
        var nextRun = existing.NextRunAtUtc;
        if (!string.Equals(updatedCronExpression, existing.CronExpression, StringComparison.Ordinal)
            || (enabled && !existing.IsEnabled))
        {
            nextRun = GetNextRunAtUtc(cron, DateTimeOffset.UtcNow);
        }

        var updated = existing with
        {
            CronExpression = updatedCronExpression,
            ParametersJson = updatedParametersJson,
            IsEnabled = enabled,
            NextRunAtUtc = nextRun,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        if (!await _repository.UpdateAsync(updated, cancellationToken).ConfigureAwait(false)) return null;
        WakeWorker();
        return updated;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var deleted = await _repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (deleted) WakeWorker();
        return deleted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var jobs = await _repository.GetEnabledAsync(stoppingToken).ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                DateTimeOffset? nextRun = null;

                foreach (var job in jobs)
                {
                    var candidate = job.NextRunAtUtc <= now
                        ? await RunJobAsync(job, now, stoppingToken).ConfigureAwait(false)
                        : job.NextRunAtUtc;
                    if (candidate is not null && (nextRun is null || candidate < nextRun))
                    {
                        nextRun = candidate;
                    }
                }

                var delay = nextRun is null
                    ? MaxWaitInterval
                    : TimeSpan.FromMinutes(Math.Min((nextRun.Value - DateTimeOffset.UtcNow).TotalMinutes, MaxWaitInterval.TotalMinutes));
                if (delay <= TimeSpan.Zero)
                {
                    continue;
                }

                await _wakeSignal.WaitAsync(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Cron job scheduler cycle failed.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    public override void Dispose()
    {
        _wakeSignal.Dispose();
        base.Dispose();
    }

    private async Task<DateTimeOffset?> RunJobAsync(CronJob job, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(job.JobType, out var handler))
        {
            _logger.LogError("Disabling Cron job {JobId}: no handler is registered for {JobType}.", job.Id, job.JobType);
            await _repository.DisableAsync(job.Id, cancellationToken).ConfigureAwait(false);
            return null;
        }

        DateTimeOffset nextRun;
        try
        {
            nextRun = GetNextRunAtUtc(ParseCron(job.CronExpression), now);
        }
        catch (Exception exception) when (exception is CronFormatException or FormatException or ArgumentException)
        {
            _logger.LogError(exception, "Disabling invalid Cron job {JobId}.", job.Id);
            await _repository.DisableAsync(job.Id, cancellationToken).ConfigureAwait(false);
            return null;
        }

        try
        {
            await handler.ExecuteAsync(job, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Disabling Cron job {JobId}: its parameters are invalid.", job.Id);
            await _repository.DisableAsync(job.Id, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Cron job {JobId} failed; advancing to its next occurrence.", job.Id);
        }

        // Once a handler has completed, persist its checkpoint even if shutdown has begun;
        // otherwise the same job may execute again after the next application start.
        await _repository.MarkRunAsync(job.Id, now, nextRun, CancellationToken.None).ConfigureAwait(false);
        return nextRun;
    }

    private static CronExpression ParseCron(string cronExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
        var expression = cronExpression.Trim();
        var fieldCount = expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var format = fieldCount switch
        {
            5 => CronFormat.Standard,
            6 => CronFormat.IncludeSeconds,
            _ => throw new FormatException("Cron expression must contain five fields, or six fields when the first field specifies seconds.")
        };
        return CronExpression.Parse(expression, format);
    }

    private static DateTimeOffset GetNextRunAtUtc(CronExpression cron, DateTimeOffset after)
    {
        return cron.GetNextOccurrence(after, TimeZoneInfo.Local)?.ToUniversalTime()
            ?? throw new FormatException("The cron expression has no occurrence within the supported search range.");
    }

    private void ValidateParameters(string jobType, string parametersJson)
    {
        if (_handlers.TryGetValue(jobType, out var handler) && handler is ICronJobParameterValidator validator)
        {
            validator.ValidateParametersJson(parametersJson);
        }
    }

    private void WakeWorker()
    {
        try
        {
            _wakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // An existing signal already causes the worker to reload its schedule.
        }
    }
}
