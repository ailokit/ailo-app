using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ailo.AI;
using Ailo.AI.Conversations;
using Ailo.Localization;
using Ailo.ViewModels;

namespace Ailo.Views;

public partial class ConversationMessagesView : UserControl
{
    private const double BottomTolerance = 1;
    private bool _followTail = true;
    private bool _scrollPending;
    private ChatMessageViewModel? _contextMessage;
    private MarkdownView? _contextMarkdownView;
    private ChatWindowViewModel? _viewModel;

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<ConversationMessagesView, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<LocalizationService?> LocalizationProperty =
        AvaloniaProperty.Register<ConversationMessagesView, LocalizationService?>(nameof(Localization));

    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public LocalizationService? Localization { get => GetValue(LocalizationProperty); private set => SetValue(LocalizationProperty, value); }

    public ConversationMessagesView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollMessagesToEndRequested -= OnScrollMessagesToEndRequested;
        }

        _viewModel = DataContext as ChatWindowViewModel;
        Localization = DataContext switch
        {
            ChatWindowViewModel chatWindow => chatWindow.Localization,
            SettingsViewModelBase settings => settings.Localization,
            _ => null
        };
        if (_viewModel is not null)
        {
            _viewModel.ScrollMessagesToEndRequested += OnScrollMessagesToEndRequested;
        }
    }

    private void OnScrollMessagesToEndRequested(object? sender, EventArgs e)
    {
        // A newly sent user message intentionally resumes tail following, even if
        // the user had previously scrolled up to inspect conversation history.
        _followTail = true;
        RequestScrollToEnd();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed)
        {
            return;
        }

        var bubble = (e.Source as Visual)?.FindAncestorOfType<Border>(true);
        while (bubble is not null && !bubble.Classes.Contains("chat-bubble"))
        {
            bubble = bubble.FindAncestorOfType<Border>();
        }

        if (bubble?.ContextMenu is not { } menu)
        {
            return;
        }

        _contextMessage = bubble.DataContext as ChatMessageViewModel;
        _contextMarkdownView = bubble.GetVisualDescendants().OfType<MarkdownView>().FirstOrDefault();
        if (Localization is { } localization)
        {
            var items = menu.Items.OfType<MenuItem>().ToList();
            if (items.Count >= 2)
            {
                items[0].Header = localization["CopySelectedText"];
                items[1].Header = localization["CopyMarkdownContent"];
            }
        }

        menu.Open(bubble);
        e.Handled = true;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => RequestScrollToEnd();

    private void OnMessagesScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Content growth does not move the offset, so it must not disable tail
        // following. Only a position change represents scrolling by the user
        // or the requested scroll-to-end operation.
        if (e.OffsetDelta.Y != 0)
        {
            _followTail = IsAtBottom();
        }

        if (_followTail && e.ExtentDelta.Y > 0)
        {
            RequestScrollToEnd();
        }
    }

    private bool IsAtBottom() =>
        MessagesScrollViewer.Offset.Y + MessagesScrollViewer.Viewport.Height >=
        MessagesScrollViewer.Extent.Height - BottomTolerance;

    private void RequestScrollToEnd()
    {
        if (!_followTail || _scrollPending)
        {
            return;
        }

        _scrollPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollPending = false;
            if (_followTail)
            {
                MessagesScrollViewer.ScrollToEnd();
            }
        }, DispatcherPriority.Render);
    }

    private async void OnCopySelectedText(object? sender, RoutedEventArgs e)
    {
        if (_contextMarkdownView is not null)
        {
            await _contextMarkdownView.CopySelectionWithFormattingAsync();
        }
    }

    private async void OnCopyMarkdown(object? sender, RoutedEventArgs e)
    {
        if (_contextMessage is { } message)
        {
            await CopyTextAsync(ThinkingMarkdown.RemoveThinkingBlocks(message.Content));
        }
    }

    private async Task CopyTextAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void OnOpenMessageImage(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MessageAttachment attachment })
        {
            ImageViewerWindow.Open(
                TopLevel.GetTopLevel(this) as Avalonia.Controls.Window,
                attachment.FilePath,
                (DataContext as ChatWindowViewModel)?.Localization ?? new LocalizationService());
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollMessagesToEndRequested -= OnScrollMessagesToEndRequested;
            _viewModel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

}
