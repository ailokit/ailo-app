using Microsoft.Extensions.Logging;

namespace Ailo.Logging;

/// <summary>
/// Provides a safe bridge for static/UI code that cannot receive an ILogger through DI.
/// </summary>
public static class ExceptionLogger
{
    private static ILoggerFactory? _factory;

    public static void Configure(ILoggerFactory? factory) => Interlocked.Exchange(ref _factory, factory);

    public static void Log(
        Exception exception,
        string category,
        string message,
        LogLevel level = LogLevel.Error)
    {
        try
        {
            Volatile.Read(ref _factory)?.CreateLogger(category).Log(level, exception, message);
        }
        catch
        {
            // Logging failures must never replace the original exception or change application behavior.
        }
    }
}
