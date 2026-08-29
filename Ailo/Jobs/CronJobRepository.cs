using Ailo.Data;
using Microsoft.Data.Sqlite;

namespace Ailo.Jobs;

/// <summary>Persists Cron job definitions, JSON parameters, and execution checkpoints.</summary>
public sealed class CronJobRepository(SqliteDatabase database)
{
    public async Task<CronJob> CreateAsync(CronJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CronJobs
                (JobType, CronExpression, ParametersJson, IsEnabled, LastRunAtUtc, NextRunAtUtc, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ($jobType, $cronExpression, $parametersJson, $isEnabled, $lastRunAtUtc, $nextRunAtUtc, $createdAtUtc, $updatedAtUtc)
            RETURNING Id;
            """;
        AddParameters(command, job);
        var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return job with { Id = id };
    }

    public async Task<IReadOnlyList<CronJob>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync(enabledOnly: true, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CronJob>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync(enabledOnly: false, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CronJob>> GetAsync(bool enabledOnly, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = enabledOnly ? """
            SELECT Id, JobType, CronExpression, ParametersJson, IsEnabled, LastRunAtUtc,
                   NextRunAtUtc, CreatedAtUtc, UpdatedAtUtc
            FROM CronJobs
            WHERE IsEnabled = 1
            ORDER BY NextRunAtUtc;
            """ : """
            SELECT Id, JobType, CronExpression, ParametersJson, IsEnabled, LastRunAtUtc,
                   NextRunAtUtc, CreatedAtUtc, UpdatedAtUtc
            FROM CronJobs
            ORDER BY IsEnabled DESC, NextRunAtUtc;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var jobs = new List<CronJob>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            jobs.Add(Read(reader));
        }

        return jobs;
    }

    public async Task<CronJob?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, JobType, CronExpression, ParametersJson, IsEnabled, LastRunAtUtc,
                   NextRunAtUtc, CreatedAtUtc, UpdatedAtUtc
            FROM CronJobs
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task MarkRunAsync(
        int id,
        DateTimeOffset lastRunAtUtc,
        DateTimeOffset nextRunAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CronJobs
            SET LastRunAtUtc = $lastRunAtUtc,
                NextRunAtUtc = $nextRunAtUtc,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id AND IsEnabled = 1;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$lastRunAtUtc", lastRunAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$nextRunAtUtc", nextRunAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DisableAsync(int id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CronJobs
            SET IsEnabled = 0,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UpdateAsync(CronJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CronJobs
            SET CronExpression = $cronExpression,
                ParametersJson = $parametersJson,
                IsEnabled = $isEnabled,
                LastRunAtUtc = $lastRunAtUtc,
                NextRunAtUtc = $nextRunAtUtc,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", job.Id);
        command.Parameters.AddWithValue("$cronExpression", job.CronExpression);
        command.Parameters.AddWithValue("$parametersJson", job.ParametersJson);
        command.Parameters.AddWithValue("$isEnabled", job.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$lastRunAtUtc", job.LastRunAtUtc?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$nextRunAtUtc", job.NextRunAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", job.UpdatedAtUtc.ToUniversalTime().ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CronJobs WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    private static void AddParameters(SqliteCommand command, CronJob job)
    {
        command.Parameters.AddWithValue("$jobType", job.JobType);
        command.Parameters.AddWithValue("$cronExpression", job.CronExpression);
        command.Parameters.AddWithValue("$parametersJson", job.ParametersJson);
        command.Parameters.AddWithValue("$isEnabled", job.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$lastRunAtUtc", job.LastRunAtUtc?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$nextRunAtUtc", job.NextRunAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$createdAtUtc", job.CreatedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", job.UpdatedAtUtc.ToUniversalTime().ToString("O"));
    }

    private static CronJob Read(SqliteDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetInt64(4) != 0,
        reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
        DateTimeOffset.Parse(reader.GetString(6)),
        DateTimeOffset.Parse(reader.GetString(7)),
        DateTimeOffset.Parse(reader.GetString(8)));
}
