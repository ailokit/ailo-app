using Ailo.Logging;
using Microsoft.Extensions.Logging;

namespace Ailo.Tests;

public sealed class DailyFileLoggerProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Ailo.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Logger_WritesDailyFileAndDeletesLogsOutsideSevenDayWindow()
    {
        Directory.CreateDirectory(_root);
        var expiredLog = Path.Combine(_root, "ailo-2026-07-31.log");
        var retainedLog = Path.Combine(_root, "ailo-2026-08-01.log");
        var unrelatedFile = Path.Combine(_root, "other-2026-07-01.log");
        File.WriteAllText(expiredLog, "expired");
        File.WriteAllText(retainedLog, "retained");
        File.WriteAllText(unrelatedFile, "unrelated");

        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 7, 12, 34, 56, TimeSpan.Zero));
        using (var provider = new DailyFileLoggerProvider(_root, timeProvider: timeProvider))
        {
            var logger = provider.CreateLogger("Ailo.Tests.Category");
            logger.LogInformation("Application started with value {Value}", 42);
        }

        var currentLog = Path.Combine(_root, "ailo-2026-08-07.log");
        Assert.True(File.Exists(currentLog));
        Assert.False(File.Exists(expiredLog));
        Assert.True(File.Exists(retainedLog));
        Assert.True(File.Exists(unrelatedFile));

        var contents = ReadLogFile(currentLog);
        Assert.Contains("2026-08-07 12:34:56.000 +00:00 [INF] Ailo.Tests.Category Application started with value 42", contents);
    }

    [Fact]
    public void Logger_RollsOverWhenTheLocalDateChanges()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 7, 23, 59, 59, TimeSpan.Zero));
        using var provider = new DailyFileLoggerProvider(_root, timeProvider: timeProvider);
        var logger = provider.CreateLogger("Ailo.Tests.Category");

        logger.LogInformation("First day");
        timeProvider.SetUtcNow(new DateTimeOffset(2026, 8, 8, 0, 0, 1, TimeSpan.Zero));
        logger.LogInformation("Second day");

        Assert.Contains("First day", ReadLogFile(Path.Combine(_root, "ailo-2026-08-07.log")));
        Assert.Contains("Second day", ReadLogFile(Path.Combine(_root, "ailo-2026-08-08.log")));
    }

    [Fact]
    public void Logger_FiltersEntriesBelowMinimumLevel()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero));
        using var provider = new DailyFileLoggerProvider(_root, LogLevel.Warning, timeProvider: timeProvider);
        var logger = provider.CreateLogger("Ailo.Tests.Category");

        logger.LogInformation("Not persisted");
        logger.LogWarning("Persisted");

        var contents = ReadLogFile(Path.Combine(_root, "ailo-2026-08-07.log"));
        Assert.DoesNotContain("Not persisted", contents);
        Assert.Contains("Persisted", contents);
    }

    /// <summary>
    /// Reads a log file with a share mode compatible with the <see cref="DailyFileLoggerProvider"/>
    /// writer handle that is still open (it keeps <c>FileAccess.Write, FileShare.ReadWrite</c> until
    /// rollover or dispose). <see cref="File.ReadAllText"/> opens with <see cref="FileShare.Read"/>, which
    /// on Windows denies the provider's existing write access to the same file and throws an
    /// <see cref="IOException"/> (ERROR_SHARING_VIOLATION).
    /// </summary>
    private static string ReadLogFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void SetUtcNow(DateTimeOffset value) => now = value;
    }
}
