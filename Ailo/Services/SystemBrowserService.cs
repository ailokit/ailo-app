using System.Diagnostics;

namespace Ailo.Services;

/// <summary>Delegates web links to the platform shell so the user's default browser is used.</summary>
public sealed class SystemBrowserService : ISystemBrowserService
{
    public void Open(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps && !url.IsFile))
        {
            throw new ArgumentException("Only absolute http, https, or file URLs can be opened in the browser.", nameof(url));
        }

        // Shell execution delegates to the user's configured browser on Windows, macOS, and
        // Linux without assuming an installed browser or embedding a platform-specific command.
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = url.AbsoluteUri,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("The operating system could not open the default browser.");
    }
}
