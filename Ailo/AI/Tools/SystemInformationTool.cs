using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Ailo.AI.Tools;

/// <summary>Agent-facing tool for retrieving selected information about the local system.</summary>
public sealed class SystemInformationTool
{
    [Description("Gets selected information about the local system. informationType determines the result: CurrentTime returns the local time and time zone; OperatingSystem returns operating-system and architecture details; CurrentUser returns the currently signed-in system user. Request only the information needed for the task.")]
    public Task<string> GetSystemInformationAsync(
        [Description("The requested information: CurrentTime, OperatingSystem, or CurrentUser.")] SystemInformationType informationType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = informationType switch
        {
            SystemInformationType.CurrentTime => GetCurrentTime(),
            SystemInformationType.OperatingSystem => GetOperatingSystem(),
            SystemInformationType.CurrentUser => GetCurrentUser(),
            _ => throw new ArgumentOutOfRangeException(nameof(informationType), informationType, "Unsupported system information type.")
        };
        return Task.FromResult(result);
    }

    private static string GetCurrentTime()
    {
        var now = DateTimeOffset.Now;
        return $"Local time: {now:yyyy-MM-dd HH:mm:ss zzz}; Time zone: {TimeZoneInfo.Local.Id}";
    }

    private static string GetOperatingSystem() =>
        $"Operating system: {RuntimeInformation.OSDescription.Trim()}; System architecture: {RuntimeInformation.OSArchitecture}; Process architecture: {RuntimeInformation.ProcessArchitecture}; System version: {Environment.OSVersion.VersionString}";

    private static string GetCurrentUser() =>
        $"Current system user: {Environment.UserDomainName}\\{Environment.UserName}";
}
