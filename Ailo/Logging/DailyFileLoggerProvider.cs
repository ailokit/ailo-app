using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Ailo.Logging;

/// <summary>
/// Writes one UTF-8 log file per local calendar day and retains the most recent daily files.
/// The implementation intentionally depends only on Microsoft.Extensions.Logging so it stays
/// small and Native AOT friendly.
/// </summary>
public sealed class DailyFileLoggerProvider : ILoggerProvider
{
    public const int DefaultRetentionDays = 7;

    private const string FileNamePrefix = "ailo-";
    private const string FileNameExtension = ".log";
    private readonly object _sync = new();
    private readonly string _logsDirectory;
    private readonly TimeProvider _timeProvider;
    private readonly int _retentionDays;
    private readonly LogLevel _minimumLevel;
    private StreamWriter? _writer;
    private DateOnly? _writerDate;
    private bool _disposed;

    public DailyFileLoggerProvider(
        string logsDirectory,
        LogLevel minimumLevel = LogLevel.Information,
        int retentionDays = DefaultRetentionDays,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionDays, 1);

        _logsDirectory = Path.GetFullPath(logsDirectory);
        _minimumLevel = minimumLevel;
        _retentionDays = retentionDays;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new DailyFileLogger(this, categoryName);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ResetWriter();
        }
    }

    private bool IsEnabled(LogLevel logLevel) =>
        !_disposed && logLevel != LogLevel.None && logLevel >= _minimumLevel;

    private void Write(LogLevel level, EventId eventId, string category, string message, Exception? exception)
    {
        var timestamp = _timeProvider.GetLocalNow();
        var date = DateOnly.FromDateTime(timestamp.DateTime);

        lock (_sync)
        {
            if (_disposed || !EnsureWriter(date))
            {
                return;
            }

            try
            {
                _writer!.Write(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
                _writer.Write(" [");
                _writer.Write(GetLevelName(level));
                _writer.Write("] ");
                _writer.Write(category);
                if (eventId.Id != 0)
                {
                    _writer.Write('[');
                    _writer.Write(eventId.Id.ToString(CultureInfo.InvariantCulture));
                    _writer.Write("] ");
                }
                else
                {
                    _writer.Write(' ');
                }

                _writer.WriteLine(message);
                if (exception is not null)
                {
                    _writer.WriteLine(exception);
                }
            }
            catch (IOException)
            {
                ResetWriter();
            }
            catch (ObjectDisposedException)
            {
                ResetWriter();
            }
        }
    }

    private bool EnsureWriter(DateOnly date)
    {
        if (_writer is not null && _writerDate == date)
        {
            return true;
        }

        ResetWriter();

        try
        {
            Directory.CreateDirectory(_logsDirectory);
            DeleteExpiredLogs(date);

            var fileName = $"{FileNamePrefix}{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{FileNameExtension}";
            var stream = new FileStream(
                Path.Combine(_logsDirectory, fileName),
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
            _writerDate = date;
            return true;
        }
        catch (IOException)
        {
            ResetWriter();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            ResetWriter();
            return false;
        }
    }

    private void DeleteExpiredLogs(DateOnly currentDate)
    {
        var oldestRetainedDate = currentDate.AddDays(1 - _retentionDays);

        foreach (var filePath in Directory.EnumerateFiles(_logsDirectory, $"{FileNamePrefix}*{FileNameExtension}"))
        {
            var fileName = Path.GetFileName(filePath);
            var dateTextLength = fileName.Length - FileNamePrefix.Length - FileNameExtension.Length;
            if (dateTextLength != 10)
            {
                continue;
            }

            var dateText = fileName.AsSpan(FileNamePrefix.Length, dateTextLength);
            if (!DateOnly.TryParseExact(
                    dateText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var fileDate) ||
                fileDate >= oldestRetainedDate)
            {
                continue;
            }

            try
            {
                File.Delete(filePath);
            }
            catch (IOException)
            {
                // A locked stale log can be retried at the next application start or day rollover.
            }
            catch (UnauthorizedAccessException)
            {
                // Logging must never prevent the application from starting.
            }
        }
    }

    private void ResetWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (IOException)
        {
            // There is no secondary sink to report file-system failures to.
        }

        _writer = null;
        _writerDate = null;
    }

    private static string GetLevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "NON"
    };

    private sealed class DailyFileLogger(DailyFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception is null)
            {
                return;
            }

            provider.Write(logLevel, eventId, category, message, exception);
        }
    }
}
