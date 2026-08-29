using Ailo.Data;
using Ailo.Jobs;
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
}
