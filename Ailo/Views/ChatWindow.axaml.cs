using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Ailo.AI.Tools;
using Ailo.Logging;
using Ailo.Services;
using Ailo.ViewModels;
using Microsoft.Extensions.Logging;

namespace Ailo.Views;

internal partial class ChatWindow : Window
{
    private readonly ChatWindowViewModel _viewModel;
    private readonly IGlobalHotKeyService _globalHotKeys;
    private readonly ILogger<ChatWindow> _logger;
    private readonly TaskCompletionSource _initializedTcs = new();

    /// <summary>Completes when the ViewModel has finished its initial <see cref="ChatWindowViewModel.LoadAsync"/>.</summary>
    public Task Initialization => _initializedTcs.Task;

    /// <summary>If set before the window is shown, this conversation will be opened after loading.</summary>
    internal string? PendingConversationId { get; set; }

    protected override bool HideOnClose => false;

    public ChatWindow(ChatWindowViewModel viewModel, IGlobalHotKeyService globalHotKeys, ILogger<ChatWindow> logger)
    {
        _viewModel = viewModel;
        _globalHotKeys = globalHotKeys;
        _logger = logger;
        DataContext = viewModel;
        viewModel.AttachStorageProvider(StorageProvider);
        InitializeComponent();
        DraftTextBox.AddHandler(KeyDownEvent, OnDraftKeyDown, RoutingStrategies.Tunnel);
        DraftTextBox.AddHandler(TextBox.PastingFromClipboardEvent, OnDraftPastingFromClipboard, RoutingStrategies.Tunnel);
        ConfigurePlatformTitleBar();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            // The window is shown first so long-running I/O never delays native window creation.
            if (PendingConversationId is not null)
            {
                await _viewModel.OpenConversationAsync(PendingConversationId).ConfigureAwait(true);
                PendingConversationId = null;
            }

            // Register global hotkeys after the ViewModel has loaded its shortcut settings.
            if (!_globalHotKeys.Start(_viewModel.ChatShortcut, _viewModel.NewChatWindowShortcut) && _globalHotKeys.LastError is not null)
            {
                _logger.LogWarning("Global hotkey registration failed: {Error}", _globalHotKeys.LastError);
            }
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ChatWindow), "Failed to initialize a chat window");
        }
        finally
        {
            _initializedTcs.TrySetResult();
        }
    }

    private void OnDraftTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            _viewModel.Draft = textBox.Text ?? string.Empty;
        }
    }

    private void OnDraftKeyDown(object? sender, KeyEventArgs e)
    {
        // Some platform backends do not raise TextBox.PastingFromClipboard for
        // a keyboard paste. Intercept the standard paste gestures in the
        // tunnel phase so image data is handled before TextBox inserts text.
        if (_viewModel.ShowAddAttachmentButton && IsPasteGesture(e))
        {
            e.Handled = true;
            _ = PasteClipboardContentAsync(DraftTextBox);
            return;
        }

        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }

        if (_viewModel.SendCommand.CanExecute(null))
        {
            _viewModel.SendCommand.Execute(null);
        }

        // Prevent TextBox from inserting a newline and prevent the window's
        // default button handling from processing the same key press.
        e.Handled = true;
    }

    private static bool IsPasteGesture(KeyEventArgs e) => e.Key == Key.V &&
        (OperatingSystem.IsMacOS()
            ? e.KeyModifiers.HasFlag(KeyModifiers.Meta)
            : e.KeyModifiers.HasFlag(KeyModifiers.Control));

    private void OnDraftPastingFromClipboard(object? sender, RoutedEventArgs e)
    {
        // Keep the regular TextBox paste behavior for models that cannot accept images.
        if (!_viewModel.ShowAddAttachmentButton || sender is not TextBox textBox)
        {
            return;
        }

        // Clipboard access is asynchronous. Handle the event here, then reproduce a
        // text paste ourselves when the clipboard does not contain an image or image file.
        e.Handled = true;
        _ = PasteClipboardContentAsync(textBox);
    }

    private async Task PasteClipboardContentAsync(TextBox textBox)
    {
        if (Clipboard is null)
        {
            return;
        }

        try
        {
            // Finder, Explorer and file managers expose copied files separately
            // from bitmap data. Prefer the original file over its potential
            // thumbnail bitmap, then fall back to a copied image or text.
            var files = await Clipboard.TryGetFilesAsync();
            var imagePaths = files?
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path) && IsSupportedImagePath(path))
                .Cast<string>()
                .ToList();
            if (imagePaths is { Count: > 0 })
            {
                await _viewModel.AddAttachmentsAsync(imagePaths);
                return;
            }

            using var bitmap = await Clipboard.TryGetBitmapAsync();
            if (bitmap is not null)
            {
                await using var imageStream = new MemoryStream();
                bitmap.Save(imageStream, PngBitmapEncoderOptions.Default);
                imageStream.Position = 0;
                await _viewModel.AddClipboardImageAsync(imageStream);
                return;
            }

            var text = await Clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                InsertPastedText(textBox, text);
            }
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ChatWindow), "Failed to paste clipboard content");
        }
    }

    private static void InsertPastedText(TextBox textBox, string text)
    {
        var current = textBox.Text ?? string.Empty;
        var start = Math.Clamp(textBox.SelectionStart, 0, current.Length);
        var end = Math.Clamp(textBox.SelectionEnd, start, current.Length);
        textBox.Text = string.Concat(current.AsSpan(0, start), text, current.AsSpan(end));
        textBox.CaretIndex = start + text.Length;
    }

    private static bool IsSupportedImagePath(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";

    private void OnOpenDraftAttachmentImage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AttachmentViewModel attachment })
        {
            ImageViewerWindow.Open(this, attachment.FilePath, _viewModel.Localization);
        }
    }

    private void OnWorkspaceDragOver(object? sender, DragEventArgs e)
    {
        // Only advertise a drop target for native files/folders; text and URLs retain
        // their normal drag behavior and never become implicit workspace permissions.
        if (e.DataTransfer.TryGetFiles() is { Length: > 0 })
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.None;
    }

    private void OnWorkspaceDrop(object? sender, DragEventArgs e)
    {
        var droppedEntries = e.DataTransfer.TryGetFiles()?
            .Select(item => item.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new WorkspaceEntry(path!, Directory.Exists(path!)))
            .ToArray();
        if (droppedEntries is not { Length: > 0 })
        {
            return;
        }

        try
        {
            _viewModel.AddWorkspaceEntries(droppedEntries);
            e.Handled = true;
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ChatWindow), "Failed to add dropped workspace entries");
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (_viewModel.IsChatShortcut(e.Key, e.KeyModifiers))
        {
            _viewModel.ShowChatCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // The base window disposes the ViewModel (DataContext) and the scope,
        // which triggers full cleanup of all window-scoped resources.
        base.OnClosed(e);
    }

}
