using Ailo.Data;
using Ailo.Jobs;

namespace Ailo.Tests;

public sealed class CronJobRepositoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task PersistsJobParametersAndUpdatesItsCheckpoint()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        var repository = new CronJobRepository(database);
        var created = DateTimeOffset.UtcNow;
        var job = new CronJob(
            0, "test", "0 9 * * 1-5", "{\"name\":\"value\"}", true, null,
            created.AddHours(1), created, created, IsOneTime: true);

        job = await repository.CreateAsync(job);
        Assert.True(job.Id > 0);
        var saved = await repository.GetByIdAsync(job.Id);
        await repository.MarkRunAsync(job.Id, created.AddMinutes(1), created.AddHours(2));
        var updated = await repository.GetByIdAsync(job.Id);

        Assert.Equal(job, saved);
        Assert.True(saved!.IsOneTime);
        Assert.Equal(created.AddMinutes(1), updated!.LastRunAtUtc);
        Assert.Equal(created.AddHours(2), updated.NextRunAtUtc);
    }

    [Fact]
    public async Task ListsAllJobsUpdatesDefinitionAndDeletesJob()
    {
        var database = new SqliteDatabase(_databasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        var repository = new CronJobRepository(database);
        var created = DateTimeOffset.UtcNow;
        var job = new CronJob(
            0, "test", "0 9 * * 1-5", "{\"name\":\"value\"}", false, created.AddMinutes(-1),
            created.AddHours(1), created, created);

        job = await repository.CreateAsync(job);
        var all = await repository.GetAllAsync();
        var updated = job with
        {
            CronExpression = "0 10 * * 1-5",
            ParametersJson = "{\"name\":\"updated\"}",
            IsEnabled = true,
            UpdatedAtUtc = created.AddMinutes(2)
        };

        Assert.Single(all);
        Assert.False(all[0].IsEnabled);
        Assert.True(await repository.UpdateAsync(updated));
        Assert.Equal(updated, await repository.GetByIdAsync(job.Id));
        Assert.True(await repository.DeleteAsync(job.Id));
        Assert.Null(await repository.GetByIdAsync(job.Id));
        Assert.False(await repository.DeleteAsync(job.Id));
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
