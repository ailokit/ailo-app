using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Runtime.Versioning;
using System.Xml.Linq;
using Microsoft.Win32;
using Ailo.Logging;
using Microsoft.Extensions.Logging;

namespace Ailo.Services;

public sealed class StartupService : IStartupService
{
    private const string WindowsRunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

    public bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return IsWindowsStartupEnabled();
            }

            return GetStartupPath() switch
            {
                StartupPath.MacOs => File.Exists(GetMacLaunchAgentPath()),
                StartupPath.Linux => File.Exists(GetLinuxAutostartPath()),
                _ => false
            };
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            ExceptionLogger.Log(exception, nameof(StartupService), "Failed to read startup setting", LogLevel.Warning);
            return false;
        }
    }

    public bool TrySetEnabled(bool enabled)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                SetWindowsStartupEnabled(enabled);
                return true;
            }

            switch (GetStartupPath())
            {
                case StartupPath.MacOs:
                    SetMacStartupEnabled(enabled);
                    break;
                case StartupPath.Linux:
                    SetLinuxStartupEnabled(enabled);
                    break;
                default:
                    return false;
            }

            return GetStartupPath() is StartupPath.MacOs or StartupPath.Linux;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException or InvalidOperationException)
        {
            ExceptionLogger.Log(exception, nameof(StartupService), "Failed to change startup setting", LogLevel.Warning);
            return false;
        }
    }

    public void OpenPermissionSettings()
    {
        try
        {
            switch (GetStartupPath())
            {
                case StartupPath.Windows:
                    OpenUri("ms-settings:startupapps");
                    break;
                case StartupPath.MacOs:
                    OpenUri("x-apple.systempreferences:com.apple.LoginItems-Settings.extension");
                    break;
                case StartupPath.Linux:
                    OpenUri(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart"));
                    break;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            ExceptionLogger.Log(exception, nameof(StartupService), "Failed to open startup permission settings", LogLevel.Warning);
            // Opening the system settings is best effort; the setting itself remains unchanged.
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(WindowsRunKey, writable: false);
        return key?.GetValue(AppIdentity.ApplicationName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindowsStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(WindowsRunKey, writable: true)
            ?? throw new UnauthorizedAccessException("Unable to open the Windows startup registry key.");

        if (enabled)
        {
            key.SetValue(AppIdentity.ApplicationName, Quote(Environment.ProcessPath ?? throw new InvalidOperationException("The process path is unavailable.")));
        }
        else
        {
            key.DeleteValue(AppIdentity.ApplicationName, throwOnMissingValue: false);
        }
    }

    private static void SetMacStartupEnabled(bool enabled)
    {
        var path = GetMacLaunchAgentPath();
        if (!enabled)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The process path is unavailable.");
        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("plist",
                new XAttribute("version", "1.0"),
                new XElement("dict",
                    new XElement("key", "Label"),
                    new XElement("string", AppIdentity.MacBundleIdentifier),
                    new XElement("key", "ProgramArguments"),
                    new XElement("array", new XElement("string", executablePath)),
                    new XElement("key", "RunAtLoad"),
                    new XElement("true"),
                    new XElement("key", "ProcessType"),
                    new XElement("string", "Interactive"))));
        document.Save(path);
    }

    private static void SetLinuxStartupEnabled(bool enabled)
    {
        var path = GetLinuxAutostartPath();
        if (!enabled)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The process path is unavailable.");
        File.WriteAllText(path, $"[Desktop Entry]{Environment.NewLine}Type=Application{Environment.NewLine}Name={AppIdentity.ApplicationName}{Environment.NewLine}Exec={Quote(executablePath)}{Environment.NewLine}X-GNOME-Autostart-enabled=true{Environment.NewLine}");
    }

    private static string GetMacLaunchAgentPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", $"{AppIdentity.MacBundleIdentifier}.plist");

    private static string GetLinuxAutostartPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "autostart", "ailo.desktop");

    private static StartupPath GetStartupPath() =>
        OperatingSystem.IsWindows() ? StartupPath.Windows :
        OperatingSystem.IsMacOS() ? StartupPath.MacOs :
        OperatingSystem.IsLinux() ? StartupPath.Linux : StartupPath.Unsupported;

    private static void OpenUri(string uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true
        });
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private enum StartupPath
    {
        Unsupported,
        Windows,
        MacOs,
        Linux
    }
}
