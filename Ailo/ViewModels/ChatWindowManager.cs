using Ailo.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Ailo.ViewModels;

public sealed class ChatWindowManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppState _appState;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IServiceScope? _groupScope;
    private readonly List<Window> _chatWindows = [];

    public ChatWindowManager(
        IServiceScopeFactory scopeFactory, AppState appState)
    {
        _scopeFactory = scopeFactory;
        _appState = appState;
    }

    public void Show()
    {
        var window = _chatWindows.LastOrDefault();
        if (window == null)
        {
            ShowNew();
            return;
        }

        // The global "show chat" shortcut must also restore an existing
        // window. Previously it was a no-op whenever a chat window existed
        // (including a minimized or hidden one), which was most visible on
        // macOS because the app normally remains resident in the menu bar.
        if (window.WindowState == Avalonia.Controls.WindowState.Minimized)
        {
            window.WindowState = Avalonia.Controls.WindowState.Normal;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
        window.Focus();
        (window as ChatWindow)?.FocusDraftTextBox();
    }

    public void ShowNew()
    {
        ShowNew(null);
    }

    public void ShowNew(string? conversationId)
    {
        var window = CreateWindow();
        window.Show();
        window.Focus();
        (window as ChatWindow)?.FocusDraftTextBox();

        _ = InitialWindowAsync(window, conversationId);
    }

    private ChatWindow CreateWindow()
    {
        _lock.Wait();

        try
        {
            // Create the shared window-group scope for the first window.
            if (_groupScope == null)
            {
                var scope = _scopeFactory.CreateScope();
                try
                {
                    _groupScope = scope;
                    _ = _appState.RefreshConversationHistoryAsync().ConfigureAwait(false);
                }
                catch
                {
                    scope.Dispose();
                    throw;
                }
            }

            // Create an independent scope for each window.
            var windowScope =
                _scopeFactory.CreateAsyncScope();

            try
            {
                var viewModel =
                    ActivatorUtilities.CreateInstance<ChatWindowViewModel>(
                        windowScope.ServiceProvider);

                var window =
                    ActivatorUtilities.CreateInstance<ChatWindow>(
                        windowScope.ServiceProvider,
                        viewModel);
                AttachWindowLifetime(
                    window,
                    windowScope);
                _chatWindows.Add(window);

                return window;
            }
            catch
            {
                windowScope.Dispose();
                throw;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task InitialWindowAsync(Window window, string? conversationId = null)
    {
        var vm = window.DataContext as ChatWindowViewModel;
        if (vm is null)
        {
            return;
        }

        if (conversationId != null)
        {
            await vm.OpenConversationAsync(conversationId);
        }
        else
        {
            // Fresh window: start an empty session with the default (first) skill.
            vm.PrepareNewSession();
        }
    }

    private void AttachWindowLifetime(
        ChatWindow window,
        AsyncServiceScope windowScope)
    {
        EventHandler? handler = null;

        handler = (_, _) =>
        {
            window.Closed -= handler;

            _ = ReleaseWindowAsync(window, windowScope);
        };

        window.Closed += handler;
    }

    private async Task ReleaseWindowAsync(
        Window window,
        AsyncServiceScope windowScope)
    {
        // ---------------------------------
        // Release this window's resources first.
        // ---------------------------------

        await windowScope.DisposeAsync();

        IServiceScope? groupScopeToDispose = null;

        await _lock.WaitAsync();

        try
        {
            _chatWindows.Remove(window);

            if (_chatWindows.Count == 0)
            {
                groupScopeToDispose = _groupScope;
                _groupScope = null;
                _appState.ClearConversationHistory();
            }
        }
        finally
        {
            _lock.Release();
        }

        // ---------------------------------
        // Dispose the shared scope only after the last window closes.
        // ---------------------------------
        groupScopeToDispose?.Dispose();
    }
}
