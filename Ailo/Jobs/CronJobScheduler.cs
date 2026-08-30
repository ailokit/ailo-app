using System.Collections.Concurrent;
using System.Text.Json;
using Ailo.Services;
using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ailo.Jobs;

/// <summary>Runs persisted Cron jobs with an isolated lifecycle for each job occurrence.</summary>
public sealed class CronJobScheduler : BackgroundService
{
    private static readonly TimeSpan MaxWaitInterval = TimeSpan.FromMinutes(5);
    private readonly CronJobRepository _repository;
    private readonly IReadOnlyDictionary<string, ICronJobHandler> _handlers;
    private readonly ILogger<CronJobScheduler> _logger;
    private readonly AppSettingsService? _settings;
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);
    private readonly ConcurrentDictionary<int, RunningJob> _runningJobs = new();

    public CronJobScheduler(
        CronJobRepository repository,
        IEnumerable<ICronJobHandler> handlers,
        ILogger<CronJobScheduler> logger,
        AppSettingsService? settings = null)
    {
        _repository = repository;
        _handlers = handlers.ToDictionary(handler => handler.JobType, StringComparer.Ordinal);
        _logger = logger;
        _settings = settings;
    }

    /// <summary>Persists a five-field or seconds-enabled six-field Cron job with serialized JSON parameters.</summary>
    public async Task<CronJob> ScheduleAsync(
        string jobType,
        string cronExpression,
        string parametersJson,
        CancellationToken cancellationToken = default,
        bool isOneTime = false)
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
            now,
            isOneTime);

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
        CancellationToken cancellationToken = default,
        bool? isOneTime = null)
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
            IsOneTime = isOneTime ?? existing.IsOneTime,
            NextRunAtUtc = nextRun,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        if (!await _repository.UpdateAsync(updated, cancellationToken).ConfigureAwait(false)) return null;
        if (!enabled)
        {
            CancelRunningJob(id);
        }
        WakeWorker();
        return updated;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var deleted = await _repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (deleted) WakeWorker();
        if (deleted) CancelRunningJob(id);
        return deleted;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var stopTask = base.StopAsync(cancellationToken);
        CancelAllRunningJobs();
        await stopTask.ConfigureAwait(false);

        var executions = _runningJobs.Values.Select(static execution => execution.Completion).ToArray();
        if (executions.Length > 0)
        {
            await Task.WhenAll(executions).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var jobs = await _repository.GetEnabledAsync(stoppingToken).ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                var maxRuntime = _settings is null
                    ? AppSettingsService.DefaultJobMaxRuntime
                    : await _settings.GetJobMaxRuntimeAsync(stoppingToken).ConfigureAwait(false);
                DateTimeOffset? nextRun = null;

                foreach (var job in jobs)
                {
                    var candidate = job.NextRunAtUtc <= now
                        ? await StartDueJobAsync(job, now, maxRuntime, stoppingToken).ConfigureAwait(false)
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
        CancelAllRunningJobs();
        _wakeSignal.Dispose();
        base.Dispose();
    }

    private async Task<DateTimeOffset?> StartDueJobAsync(
        CronJob job,
        DateTimeOffset now,
        TimeSpan maxRuntime,
        CancellationToken stoppingToken)
    {
        if (!_handlers.TryGetValue(job.JobType, out var handler))
        {
            await DisableInvalidJobAsync(job, $"no handler is registered for {job.JobType}", stoppingToken).ConfigureAwait(false);
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
            await _repository.DisableAsync(job.Id, stoppingToken).ConfigureAwait(false);
            return null;
        }

        if (_runningJobs.ContainsKey(job.Id))
        {
            return nextRun;
        }

        var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        jobCancellation.CancelAfter(maxRuntime);
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = new RunningJob(jobCancellation, completion.Task);
        if (!_runningJobs.TryAdd(job.Id, execution))
        {
            jobCancellation.Dispose();
            return nextRun;
        }

        // LongRunning gives every occurrence its own worker thread. The handler's async continuations
        // may resume on the pool, but this lifecycle is never awaited by the scheduler loop or another job.
        _ = Task.Factory.StartNew(
            async () =>
            {
                try
                {
                    await RunJobAsync(job, handler, now, nextRun, maxRuntime, execution, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Cron job {JobId} lifecycle failed.", job.Id);
                }
                finally
                {
                    _runningJobs.TryRemove(new KeyValuePair<int, RunningJob>(job.Id, execution));
                    jobCancellation.Dispose();
                    completion.TrySetResult(null);
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();

        return nextRun;
    }

    private async Task RunJobAsync(
        CronJob job,
        ICronJobHandler handler,
        DateTimeOffset now,
        DateTimeOffset nextRun,
        TimeSpan maxRuntime,
        RunningJob execution,
        CancellationToken stoppingToken)
    {
        var cancellationToken = execution.Cancellation.Token;
        var timedOut = false;
        try
        {
            await handler.ExecuteAsync(job, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown intentionally leaves the checkpoint untouched so an interrupted run can retry
            // after the next application start.
            return;
        }
        catch (OperationCanceledException) when (execution.WasManuallyCancelled)
        {
            // Disabling or deleting a job cancels only that job's lifecycle. Its disabled/deleted
            // definition must not receive a new checkpoint.
            return;
        }
        catch (OperationCanceledException) when (execution.Cancellation.IsCancellationRequested)
        {
            timedOut = true;
            _logger.LogWarning(
                "Cron job {JobId} exceeded the global maximum runtime of {MaxRuntime}.",
                job.Id,
                maxRuntime);
        }
        catch (JsonException exception)
        {
            if (job.IsOneTime)
            {
                _logger.LogError(exception, "Deleting one-time Cron job {JobId}: its parameters are invalid.", job.Id);
                await _repository.DeleteAsync(job.Id, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                _logger.LogError(exception, "Disabling Cron job {JobId}: its parameters are invalid.", job.Id);
                await _repository.DisableAsync(job.Id, CancellationToken.None).ConfigureAwait(false);
            }
            return;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Cron job {JobId} failed; advancing to its next occurrence.", job.Id);
        }

        if (timedOut)
        {
            _logger.LogWarning("Cron job {JobId} was cancelled after reaching its maximum runtime.", job.Id);
        }

        if (job.IsOneTime)
        {
            await _repository.DeleteAsync(job.Id, CancellationToken.None).ConfigureAwait(false);
            _logger.LogInformation("One-time Cron job {JobId} was deleted after execution.", job.Id);
            return;
        }

        // Once a handler has completed or timed out, persist its checkpoint. Shutdown is the only
        // cancellation that skips this so the interrupted occurrence can retry after restart.
        await _repository.MarkRunAsync(job.Id, now, nextRun, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task DisableInvalidJobAsync(CronJob job, string reason, CancellationToken cancellationToken)
    {
        _logger.LogError("Disabling Cron job {JobId}: {Reason}.", job.Id, reason);
        await _repository.DisableAsync(job.Id, cancellationToken).ConfigureAwait(false);
    }

    private void CancelRunningJob(int id)
    {
        if (_runningJobs.TryGetValue(id, out var execution))
        {
            execution.CancelManually();
        }
    }

    private void CancelAllRunningJobs()
    {
        foreach (var execution in _runningJobs.Values)
        {
            execution.CancelManually();
        }
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

    private sealed class RunningJob(CancellationTokenSource cancellation, Task completion)
    {
        private int _wasManuallyCancelled;

        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Completion { get; } = completion;
        public bool WasManuallyCancelled => Volatile.Read(ref _wasManuallyCancelled) != 0;

        public void CancelManually()
        {
            Interlocked.Exchange(ref _wasManuallyCancelled, 1);
            try
            {
                Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The execution completed between the dictionary snapshot and cancellation.
            }
        }
    }
}
