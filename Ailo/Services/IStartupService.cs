namespace Ailo.Services;

public interface IStartupService
{
    bool IsEnabled();

    bool TrySetEnabled(bool enabled);

    void OpenPermissionSettings();
}
