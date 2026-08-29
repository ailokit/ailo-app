using System.ComponentModel;
using Ailo.Services;

namespace Ailo.AI.Tools;

/// <summary>Agent-facing tool for opening a webpage in the user's default system browser.</summary>
public sealed class OpenWebpageTool(ISystemBrowserService systemBrowser, ChatWorkspace workspace)
{
    [Description("Opens an http or https webpage, or an existing local .html/.htm file inside the authorized workspace, in the user's default system browser. Use this only when the user asks to open, visit, or view a webpage externally.")]
    public string OpenWebpageInBrowser(
        [Description("The absolute http or https webpage URL, an absolute local .html/.htm file path, or a file:// URI for an authorized local HTML file.")] string url)
    {
        if (Path.IsPathFullyQualified(url))
        {
            return OpenLocalHtmlFile(url);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "The target must be an absolute http/https URL or an absolute local HTML file path.";
        }

        if (uri.IsFile)
        {
            return uri.IsUnc ? "Network file URLs are not supported." : OpenLocalHtmlFile(uri.LocalPath);
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return "The URL must be an absolute http or https address without embedded credentials.";
        }

        systemBrowser.Open(uri);
        return $"Opened {uri.AbsoluteUri} in the system browser.";
    }

    private string OpenLocalHtmlFile(string path)
    {
        string authorizedPath;
        try
        {
            authorizedPath = WorkspacePathSecurity.Authorize(workspace, path);
        }
        catch (UnauthorizedAccessException)
        {
            return "The local HTML file must be inside the selected workspace.";
        }

        if (!File.Exists(authorizedPath))
        {
            return "The local HTML file does not exist.";
        }

        var extension = Path.GetExtension(authorizedPath);
        if (!string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase))
        {
            return "Only local .html or .htm files can be opened in the browser.";
        }

        var fileUri = new Uri(authorizedPath);
        systemBrowser.Open(fileUri);
        return $"Opened {fileUri.AbsoluteUri} in the system browser.";
    }
}
