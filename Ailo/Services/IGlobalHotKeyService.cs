namespace Ailo.Services;

public interface IGlobalHotKeyService : IDisposable
{
    bool Start(string chatShortcut, string newChatWindowShortcut);
    void UpdateShortcuts(string chatShortcut, string newChatWindowShortcut);
    string? LastError { get; }
}
