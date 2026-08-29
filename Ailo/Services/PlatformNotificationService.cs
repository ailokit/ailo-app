using System.Diagnostics;
using System.Text;

namespace Ailo.Services;

/// <summary>Bridges recurring notifications to the native notification mechanism of each desktop platform.</summary>
public sealed class PlatformNotificationService : IPlatformNotificationService
{
    public Task ShowAsync(string title, string body, string? subtitle = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        if (OperatingSystem.IsMacOS())
        {
            var script = $"display notification {AppleScriptString(body)} with title {AppleScriptString(title)}";
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                script += $" subtitle {AppleScriptString(subtitle)}";
            }

            return RunProcessAsync("osascript", ["-e", script], cancellationToken);
        }

        if (OperatingSystem.IsWindows())
        {
            return ShowWindowsNotificationAsync(title, body, subtitle, cancellationToken);
        }

        if (OperatingSystem.IsLinux())
        {
            return RunProcessAsync("notify-send", ["--app-name", AppIdentity.ApplicationName, "--urgency", "normal", title, body], cancellationToken);
        }

        throw new PlatformNotSupportedException("Native notifications are not supported on this platform.");
    }

    private static Task ShowWindowsNotificationAsync(string title, string body, string? subtitle, CancellationToken cancellationToken)
    {
        var script = $$"""
            [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
            [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
            $title = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Base64(title)}}'))
            $subtitle = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Base64(subtitle ?? string.Empty)}}'))
            $body = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Base64(body)}}'))
            $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
            $xml.LoadXml('<toast><visual><binding template="ToastGeneric"><text></text><text></text><text></text></binding></visual></toast>')
            $textNodes = $xml.GetElementsByTagName('text')
            $textNodes.Item(0).InnerText = $title
            $textNodes.Item(1).InnerText = $subtitle
            $textNodes.Item(2).InnerText = $body
            $toast = New-Object Windows.UI.Notifications.ToastNotification($xml)
            [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Ailo').Show($toast)
            """;
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return RunProcessAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-EncodedCommand", encodedCommand], cancellationToken);
    }

    private static string Base64(string? value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static string AppleScriptString(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\"";

    private static async Task RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start notification process '{fileName}'.");
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited between HasExited and Kill.
            }
        });
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var error = (await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"Notification process '{fileName}' exited with code {process.ExitCode}."
                : $"Notification process '{fileName}' failed: {error}");
        }
    }
}
