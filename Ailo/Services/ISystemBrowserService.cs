namespace Ailo.Services;

/// <summary>Opens an absolute browser-supported URI in the operating system's default browser.</summary>
public interface ISystemBrowserService
{
    void Open(Uri url);
}
