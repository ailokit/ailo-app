using Ailo.Data;
using Ailo.Jobs;
using Ailo.AI.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ailo.Tests;

public sealed class AgentJobTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Ailo.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ScheduleAsync_PersistsNormalizedPromptAndWorkingDirectory()
    {
        Directory.CreateDirectory(_root);
        var database = new SqliteDatabase(Path.Combine(_root, "ailo.db"));
        await new DatabaseMigrator(database).MigrateAsync();
        var handler = new ValidatingAgentJobHandler();
        using var scheduler = new CronJobScheduler(
            new CronJobRepository(database),
            [handler],
            NullLogger<CronJobScheduler>.Instance);

        var job = await AgentJob.ScheduleAsync(scheduler, "0 9 * * 1-5", "  run the scripts  ", _root);
        var parameters = AgentJob.ParseParameters(job.ParametersJson);

        Assert.Equal(AgentJob.Type, job.JobType);
        Assert.Equal("run the scripts", parameters.Prompt);
        Assert.Equal(WorkspacePathSecurity.NormalizeEntry(_root, isDirectory: true).Path, parameters.WorkingDirectory);
        Assert.True(handler.Validated);
    }

    [Fact]
    public void CreateParameters_RejectsRelativeOrMissingDirectories()
    {
        Assert.Throws<ArgumentException>(() => AgentJob.CreateParameters("task", "relative/path"));
        Assert.Throws<DirectoryNotFoundException>(() => AgentJob.CreateParameters("task", Path.Combine(_root, "missing")));
    }

    [Fact]
    public void ParseParameters_AcceptsCamelCaseToolJson()
    {
        Directory.CreateDirectory(_root);
        var jsonPath = _root.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var parametersJson = $$"""{"prompt":"update the report","workingDirectory":"{{jsonPath}}"}""";

        var parameters = AgentJob.ParseParameters(parametersJson);

        Assert.Equal("update the report", parameters.Prompt);
        Assert.Equal(WorkspacePathSecurity.NormalizeEntry(_root, isDirectory: true).Path, parameters.WorkingDirectory);
    }

    [Fact]
    public async Task UpdateAsync_CanDisableJobAfterItsWorkingDirectoryIsRemoved()
    {
        Directory.CreateDirectory(_root);
        var workingDirectory = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(workingDirectory);
        var database = new SqliteDatabase(Path.Combine(_root, "ailo.db"));
        await new DatabaseMigrator(database).MigrateAsync();
        using var scheduler = new CronJobScheduler(
            new CronJobRepository(database),
            [new ValidatingAgentJobHandler()],
            NullLogger<CronJobScheduler>.Instance);
        var job = await AgentJob.ScheduleAsync(scheduler, "0 9 * * 1-5", "task", workingDirectory);

        Directory.Delete(workingDirectory);
        var updated = await scheduler.UpdateAsync(job.Id, isEnabled: false);

        Assert.NotNull(updated);
        Assert.False(updated!.IsEnabled);
    }

    [Fact]
    public async Task ExecutionLog_WritesAndFlushesToWorkingDirectory()
    {
        Directory.CreateDirectory(_root);
        await using var log = await AgentJobExecutionLog.CreateAsync(_root, 42, CancellationToken.None);

        await log.WriteAsync("START test", CancellationToken.None);

        Assert.Equal(Path.Combine(_root, "ailo-agent-job-42.log"), log.Path);
        await using var stream = new FileStream(
            log.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4 * 1024,
            FileOptions.Asynchronous);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        Assert.Contains("START test", content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ValidatingAgentJobHandler : ICronJobHandler, ICronJobParameterValidator
    {
        public bool Validated { get; private set; }
        public string JobType => AgentJob.Type;

        public Task ExecuteAsync(CronJob job, CancellationToken cancellationToken) => Task.CompletedTask;

        public void ValidateParametersJson(string parametersJson)
        {
            _ = AgentJob.ParseParameters(parametersJson);
            Validated = true;
        }
    }
}
