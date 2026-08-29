using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ailo.AI.Conversations;
using Ailo.Data;
using Ailo.Localization;
using Ailo.Logging;
using Ailo.Services;

namespace Ailo.ViewModels;

public sealed partial class HistorySettingsViewModel : SettingsViewModelBase
{
    private readonly ConversationRepository _conversationRepository;
    private readonly MessageRepository _messageRepository;
    private readonly ChatWindowManager _chatWindowManager;
    private readonly IConfirmationService? _confirmation;
    private readonly IWindowNavigationService? _navigation;
    private int _historyPage;
    private bool _historyHasMore = true;
    private bool _historyLoading;
    private const int HistoryPageSize = 20;

    public HistorySettingsViewModel(
        ConversationRepository conversationRepository,
        MessageRepository messageRepository,
        LocalizationService localization,
        ChatWindowManager chatWindowManager,
        IConfirmationService? confirmation = null,
        IWindowNavigationService? navigation = null)
        : base(localization)
    {
        _conversationRepository = conversationRepository;
        _messageRepository = messageRepository;
        _chatWindowManager = chatWindowManager;
        _confirmation = confirmation;
        _navigation = navigation;
    }

    public ObservableCollection<Conversation> HistoryConversations { get; } = [];
    public ObservableCollection<ChatMessageViewModel> HistoryMessages { get; } = [];

    [ObservableProperty]
    private Conversation? _selectedHistoryConversation;

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        if (_historyLoading) return;
        _historyPage = 0;
        _historyHasMore = true;
        HistoryConversations.Clear();
        HistoryMessages.Clear();
        SelectedHistoryConversation = null;
        await LoadMoreHistoryAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task LoadMoreHistoryAsync()
    {
        if (_historyLoading || !_historyHasMore) return;
        _historyLoading = true;
        try
        {
            var page = await _conversationRepository.GetPageAsync(_historyPage, HistoryPageSize).ConfigureAwait(false);
            foreach (var conversation in page) HistoryConversations.Add(conversation);
            _historyPage++;
            _historyHasMore = page.Count == HistoryPageSize;
        }
        finally { _historyLoading = false; }
    }

    [RelayCommand]
    private void StartHistoryConversation()
    {
        if (SelectedHistoryConversation is not null)
            _chatWindowManager?.ShowNew(SelectedHistoryConversation.Id);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteHistoryConversation))]
    private async Task DeleteHistoryConversationAsync()
    {
        var conversation = SelectedHistoryConversation;
        if (conversation is null) return;

        if (_confirmation is not null && !await _confirmation.ConfirmDeleteAsync(conversation.Title))
            return;

        await _conversationRepository.ArchiveAsync(conversation.Id).ConfigureAwait(false);
        await LoadHistoryAsync().ConfigureAwait(false);
        StatusMessage = T("HistoryDeleted");
    }

    private bool CanDeleteHistoryConversation() => SelectedHistoryConversation is not null;

    partial void OnSelectedHistoryConversationChanged(Conversation? value)
    {
        DeleteHistoryConversationCommand.NotifyCanExecuteChanged();
        if (value is not null) _ = ReloadSelectedHistoryAsync(value.Id);
    }

    private async Task ReloadSelectedHistoryAsync(string conversationId)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId).ConfigureAwait(false);
        if (conversation is null) return;
        SelectedHistoryConversation = conversation;
        HistoryMessages.Clear();
        foreach (var message in await _messageRepository.GetByConversationAsync(conversation.Id).ConfigureAwait(false))
            HistoryMessages.Add(CreateHistoryMessage(message));
    }

    internal static ChatMessageViewModel CreateHistoryMessage(Message message) =>
        new(message.Role, message.Content, message.Attachments);
}
