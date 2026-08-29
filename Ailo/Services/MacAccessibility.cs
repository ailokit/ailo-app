using System.Runtime.InteropServices;
using System.Diagnostics;
using Ailo.Logging;
using Microsoft.Extensions.Logging;

namespace Ailo.Services;

internal static partial class MacAccessibility
{
    public static bool IsTrusted()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return true;
        }

        try
        {
            return AXIsProcessTrusted();
        }
        catch (DllNotFoundException exception)
        {
            ExceptionLogger.Log(exception, nameof(MacAccessibility), "macOS accessibility API is unavailable", LogLevel.Warning);
            return false;
        }
    }

    public static void OpenSettings()
    {
        // Keep this guard here as well as at the call site. Never open Settings
        // for a process that already has Accessibility permission.
        if (!OperatingSystem.IsMacOS() || IsTrusted())
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                Arguments = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(MacAccessibility), "Failed to open macOS accessibility settings", LogLevel.Warning);
            // The status message still contains the manual navigation path.
        }
    }

    [LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool AXIsProcessTrusted();
}
