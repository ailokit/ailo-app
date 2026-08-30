using System.Collections.Concurrent;
using Ailo.Data;
using Ailo.Jobs;
using Ailo.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ailo.Tests;

public sealed class CronJobSchedulerTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task ExecutesDuePersistedJobAndAdvancesItsCheckpoint()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        var repository = new CronJobRepository(database);
        var executed = new TaskCompletionSource<CronJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new TestJobHandler(executed);
        using var scheduler = new CronJobScheduler(repository, [handler], NullLogger<CronJobScheduler>.Instance);
        var now = DateTimeOffset.UtcNow;
        var job = new CronJob(
            0, handler.JobType, "* * * * *", "{}", true, null,
            now.AddMinutes(-1), now.AddMinutes(-2), now.AddMinutes(-2));
        job = await repository.CreateAsync(job);

        await scheduler.StartAsync(CancellationToken.None);
        var executedJob = await executed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await scheduler.StopAsync(CancellationToken.None);
        var updated = await repository.GetByIdAsync(job.Id);

        Assert.Equal(job.Id, executedJob.Id);
        Assert.NotNull(updated!.LastRunAtUtc);
        Assert.True(updated.NextRunAtUtc > updated.LastRunAtUtc);
    }

    [Fact]
    public async Task OneTimeJob_IsDeletedAfterExecution()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        var repository = new CronJobRepository(database);
        var executed = new TaskCompletionSource<CronJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new TestJobHandler(executed);
        using var scheduler = new CronJobScheduler(repository, [handler], NullLogger<CronJobScheduler>.Instance);
        var now = DateTimeOffset.UtcNow;
        var job = await repository.CreateAsync(new CronJob(
            0, handler.JobType, "* * * * *", "{}", true, null,
            now.AddMinutes(-1), now.AddMinutes(-2), now.AddMinutes(-2), IsOneTime: true));

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            await executed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            for (var attempt = 0; attempt < 40 && await repository.GetByIdAsync(job.Id) is not null; attempt++)
            {
                await Task.Delay(25);
            }

            Assert.Null(await repository.GetByIdAsync(job.Id));
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ScheduleAsync_AcceptsSecondsEnabledCronExpressions()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        var handler = new TestJobHandler(new TaskCompletionSource<CronJob>());
        using var scheduler = new CronJobScheduler(
            new CronJobRepository(database),
            [handler],
            NullLogger<CronJobScheduler>.Instance);

        var job = await scheduler.ScheduleAsync(handler.JobType, "*/10 * * * * *", "{}");

        Assert.True(job.Id > 0);
        Assert.Equal("*/10 * * * * *", job.CronExpression);
        Assert.Equal(0, job.NextRunAtUtc.Second % 10);
        Assert.False(job.IsOneTime);

        var oneTimeJob = await scheduler.ScheduleAsync(handler.JobType, "*/10 * * * * *", "{}", CancellationToken.None, true);
        Assert.True(oneTimeJob.IsOneTime);
    }

    [Fact]
    public async Task UpdateAsync_RecalculatesScheduleAndCanDisableJob()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        var handler = new TestJobHandler(new TaskCompletionSource<CronJob>());
        using var scheduler = new CronJobScheduler(
            new CronJobRepository(database), [handler], NullLogger<CronJobScheduler>.Instance);
        var job = await scheduler.ScheduleAsync(handler.JobType, "*/10 * * * * *", "{}");

        var updated = await scheduler.UpdateAsync(job.Id, "*/20 * * * * *", "{\"updated\":true}", false);

        Assert.NotNull(updated);
        Assert.Equal("*/20 * * * * *", updated!.CronExpression);
        Assert.Equal("{\"updated\":true}", updated.ParametersJson);
        Assert.False(updated.IsEnabled);
        Assert.True(updated.NextRunAtUtc > DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.True(await scheduler.DeleteAsync(job.Id));
        Assert.False(await scheduler.DeleteAsync(job.Id));
    }

    [Fact]
    public async Task DueJobs_RunOnIndependentThreadsWithoutBlockingEachOther()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        var bothStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new BlockingJobHandler(bothStarted, release);
        using var scheduler = new CronJobScheduler(
            new CronJobRepository(database), [handler], NullLogger<CronJobScheduler>.Instance);
        var now = DateTimeOffset.UtcNow;
        await new CronJobRepository(database).CreateAsync(new CronJob(
            0, handler.JobType, "* * * * *", "{\"slot\":1}", true, null,
            now.AddMinutes(-1), now, now));
        await new CronJobRepository(database).CreateAsync(new CronJob(
            0, handler.JobType, "* * * * *", "{\"slot\":2}", true, null,
            now.AddMinutes(-1), now, now));

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(2, handler.ThreadIds.Count);
            Assert.Equal(2, handler.StartedCount);
        }
        finally
        {
            release.TrySetResult(null);
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GlobalMaximumRuntime_CancelsAndCheckpointsAJob()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        var repository = new CronJobRepository(database);
        var settings = new AppSettingsService(new AppSettingRepository(database));
        await settings.SaveJobMaxRuntimeAsync(TimeSpan.FromSeconds(1));
        var cancelled = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new TimeoutJobHandler(cancelled);
        using var scheduler = new CronJobScheduler(
            repository, [handler], NullLogger<CronJobScheduler>.Instance, settings);
        var now = DateTimeOffset.UtcNow;
        var job = await repository.CreateAsync(new CronJob(
            0, handler.JobType, "* * * * *", "{}", true, null,
            now.AddMinutes(-1), now, now));

        await scheduler.StartAsync(CancellationToken.None);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await scheduler.StopAsync(CancellationToken.None);

        var updated = await repository.GetByIdAsync(job.Id);
        Assert.NotNull(updated!.LastRunAtUtc);
        Assert.True(updated.NextRunAtUtc > updated.LastRunAtUtc);
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private sealed class TestJobHandler(TaskCompletionSource<CronJob> executed) : ICronJobHandler
    {
        public string JobType => "test";

        public Task ExecuteAsync(CronJob job, CancellationToken cancellationToken)
        {
            executed.TrySetResult(job);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingJobHandler(
        TaskCompletionSource<object?> bothStarted,
        TaskCompletionSource<object?> release) : ICronJobHandler
    {
        private int _startedCount;

        public string JobType => "blocking";
        public int StartedCount => _startedCount;
        public ConcurrentDictionary<int, byte> ThreadIds { get; } = new();

        public async Task ExecuteAsync(CronJob job, CancellationToken cancellationToken)
        {
            ThreadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
            if (Interlocked.Increment(ref _startedCount) == 2)
            {
                bothStarted.TrySetResult(null);
            }

            await release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class TimeoutJobHandler(TaskCompletionSource<object?> cancelled) : ICronJobHandler
    {
        public string JobType => "timeout";

        public async Task ExecuteAsync(CronJob job, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult(null);
                throw;
            }
        }
    }
}
