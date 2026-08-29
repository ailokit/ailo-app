using Avalonia.Threading;
using SharpHook;
using SharpHook.Data;
using SharpHook.Providers;
using Ailo.Logging;
using Ailo.ViewModels;

namespace Ailo.Services;

/// <summary>
/// Owns the process-wide keyboard hook and marshals matching shortcuts back to the UI thread.
/// </summary>
public sealed class GlobalHotKeyService(ChatWindowManager chatWindowManager)
    : IGlobalHotKeyService
{
    private readonly ChatWindowManager _chatWindowManager = chatWindowManager;
    private EventLoopGlobalHook? _hook;

    // These values are updated without recreating the hook so changing settings takes effect immediately.
    private string _chatShortcut = string.Empty;
    private string _newChatWindowShortcut = string.Empty;
    private bool _reportedMissingMacAccessibility;

    private Task? _hookTask;

    // Native hooks can report the same key press more than once; this timestamp debounces it.
    private long _lastTriggeredAt;

    /// <summary>Gets the most recent hook-start failure that can be shown to the user.</summary>
    public string? LastError { get; private set; }

    /// <summary>Starts listening for global shortcuts, if the operating system permits it.</summary>
    public bool Start(string chatShortcut, string newChatWindowShortcut)
    {
        _chatShortcut = chatShortcut;
        _newChatWindowShortcut = newChatWindowShortcut;
        if (_hook is not null)
        {
            return true;
        }

        // AXIsProcessTrusted is macOS' authoritative check for the current
        // process. Do not use libuiohook's preflight here: it can retain a
        // stale result after the user changes the Accessibility setting, which
        // made a granted release build open System Settings on every launch.
        if (OperatingSystem.IsMacOS() && !MacAccessibility.IsTrusted())
        {
            LastError = "macOS has not granted Accessibility permission to Ailo, so global hotkeys cannot be enabled. Re-enable Ailo under System Settings > Privacy & Security > Accessibility, then restart the app.";
            if (!_reportedMissingMacAccessibility)
            {
                _reportedMissingMacAccessibility = true;
                MacAccessibility.OpenSettings();
            }

            return false;
        }

        try
        {
            // We own the permission UX above. Suppress libuiohook's native
            // prompt so it cannot reopen Accessibility settings after a
            // successful system-level permission check.
            UioHookProvider.Instance.PromptUserIfAxApiDisabled = false;
            UioHookProvider.Instance.KeyTypedEnabled = false;
            _hook = new EventLoopGlobalHook(UioHookProvider.Instance);
            _hook.KeyPressed += OnKeyPressed;
            _hookTask = RunAsync(_hook);
            LastError = null;
            return true;
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(GlobalHotKeyService), "Failed to start global hotkeys");
            LastError = $"Failed to start global hotkeys: {exception.Message}";
            _hook?.Dispose();
            _hook = null;
            return false;
        }
    }

    public void UpdateShortcuts(string chatShortcut, string newChatWindowShortcut)
    {
        _chatShortcut = chatShortcut;
        _newChatWindowShortcut = newChatWindowShortcut;
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var current = Format(e.Data.KeyCode, e.RawEvent.Mask);
        if (current is null)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastTriggeredAt) < 300)
        {
            return;
        }

        // The explicit "new window" action wins if both settings resolve to the same shortcut.
        if (ShortcutFormatter.Matches(_newChatWindowShortcut, current))
        {
            Interlocked.Exchange(ref _lastTriggeredAt, now);
            Dispatcher.UIThread.Post(_chatWindowManager.ShowNew);
            return;
        }

        if (ShortcutFormatter.Matches(_chatShortcut, current))
        {
            Interlocked.Exchange(ref _lastTriggeredAt, now);
            // The "chat" shortcut shows the existing chat window (or opens one if
            // none is open); only the explicit "new window" shortcut creates one.
            Dispatcher.UIThread.Post(_chatWindowManager.Show);
        }
    }

    public void Dispose()
    {
        if (_hook is null)
        {
            return;
        }

        _hook.KeyPressed -= OnKeyPressed;
        _hook.Dispose();
        _hook = null;
    }

    private async Task RunAsync(EventLoopGlobalHook hook)
    {
        try
        {
            await hook.RunAsync(GlobalHookType.Keyboard).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(GlobalHotKeyService),
                "Global hotkey event loop stopped unexpectedly");
            // Do not leave a dead hook looking like a running hook. This is
            // especially important on macOS when TCC permission belongs to an
            // older app bundle identity.
            if (ReferenceEquals(_hook, hook))
            {
                hook.KeyPressed -= OnKeyPressed;
                hook.Dispose();
                _hook = null;
                _hookTask = null;
                LastError = OperatingSystem.IsMacOS()
                    ? $"macOS global hotkeys failed to start: {exception.Message}. Re-enable Ailo under Accessibility and Input Monitoring, then restart the app."
                    : $"Global hotkeys failed to start: {exception.Message}";
            }
        }
    }

    private static string? Format(KeyCode key, EventMask mask)
    {
        var keyName = key.ToString();
        if (!keyName.StartsWith("Vc", StringComparison.Ordinal))
        {
            return null;
        }

        keyName = keyName[2..];
        var parts = new List<string>(5);
        if ((mask & (EventMask.LeftCtrl | EventMask.RightCtrl)) != 0) parts.Add("Ctrl");
        if ((mask & (EventMask.LeftAlt | EventMask.RightAlt)) != 0) parts.Add("Alt");
        if ((mask & (EventMask.LeftShift | EventMask.RightShift)) != 0) parts.Add("Shift");
        if ((mask & (EventMask.LeftMeta | EventMask.RightMeta)) != 0) parts.Add("Meta");
        parts.Add(keyName);
        return string.Join('+', parts);
    }
}
