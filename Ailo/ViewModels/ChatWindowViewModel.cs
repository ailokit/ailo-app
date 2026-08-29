using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Ailo.AI;
using Ailo.AI.Conversations;
using Ailo.AI.Providers;
using Ailo.AI.Skills;
using Ailo.AI.Tools;
using Ailo.Data;
using Ailo.Localization;
using Ailo.Logging;
using Ailo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Microsoft.Extensions.Logging;

namespace Ailo.ViewModels;

/// <summary>
/// Presentation model for the chat window. Each <see cref="Views.ChatWindow"/> receives its own
/// instance via scoped injection within a dedicated DI scope.
/// </summary>
/// <remarks>
/// <see cref="AppState"/> is the only shared state so preferences and update progress remain
/// consistent across windows without sharing transient conversation state.
/// </remarks>
public sealed partial class ChatWindowViewModel : ViewModelBase
{
    private readonly ConversationService _conversations;
    private readonly ChatService _chat;
    private readonly ConversationRepository _conversationRepository;
    private readonly MessageRepository _messageRepository;
    private readonly ChatWindowManager _chatWindowManager;
    private readonly IWindowNavigationService? _navigation;
    private readonly LocalizationService _localization;
    private readonly IGlobalHotKeyService? _globalHotKeys;
    private readonly AppPaths _appPaths;
    private readonly ChatWorkspace _workspace;
    private IStorageProvider? _storageProvider;
    private Conversation? _conversation;
    private CancellationTokenSource? _sendCancellation;
    private bool _openingConversation;
    private bool _restoringSkillSelection;
    private string? _lastSelectedSkillId;

    public ChatWindowViewModel(
        ConversationService conversations,
        ChatService chat,
        ConversationRepository conversationRepository,
        MessageRepository messageRepository,
        AppState appState,
        ChatWindowManager chatWindowManager,
        ChatWorkspace workspace,
        IWindowNavigationService? navigation = null,
        IGlobalHotKeyService? globalHotKeys = null,
        LocalizationService? localization = null,
        AppPaths? appPaths = null
    )
    {
        AppState = appState;
        _conversations = conversations;
        _chat = chat;
        _conversationRepository = conversationRepository;
        _messageRepository = messageRepository;
        _chatWindowManager = chatWindowManager;
        _navigation = navigation;
        _localization = localization ?? new LocalizationService();
        _globalHotKeys = globalHotKeys;
        _appPaths = appPaths ?? AppPaths.CreateDefault();
        _workspace = workspace;
        Attachments.CollectionChanged += OnAttachmentsChanged;
        AppState.Tools.CollectionChanged += OnToolsCollectionChanged;
        foreach (var tool in AppState.Tools)
            tool.PropertyChanged += OnAppToolPropertyChanged;
        RebuildSessionTools();
        // Re-validate the dropdown selection whenever the shared skills list is
        // reloaded (e.g. after editing skills in the settings window).
        AppState.Skills.CollectionChanged += OnSkillsCollectionChanged;

        // Keep the window's shortcut bindings in sync with the shared app state so
        // the global hotkey hook always uses the user's configured shortcuts. This
        // also corrects the values once AppState finishes loading at startup.
        ChatShortcut = AppState.ChatShortcut;
        NewChatWindowShortcut = AppState.NewChatWindowShortcut;
        AppState.PropertyChanged += OnAppStatePropertyChanged;
    }

    public AppState AppState { get; }

    // ── Collections for chat UI ──────────────────────────────────────────

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<AttachmentViewModel> Attachments { get; } = [];

    public ObservableCollection<WorkspaceEntryViewModel> WorkspaceEntries { get; } = [];

    /// <summary>Tools enabled in application settings and selected for this chat session.</summary>
    public ObservableCollection<SessionToolSelection> SessionTools { get; } = [];

    public bool HasSessionTools => SessionTools.Count > 0;

    public IReadOnlySet<string> SelectedToolNames => SessionTools.Where(static tool => tool.IsSelected)
        .Select(static tool => tool.Name)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Raised when a newly sent message should bring the conversation view back to its latest entry.</summary>
    public event EventHandler? ScrollMessagesToEndRequested;

    // ── Chat-bound selections ────────────────────────────────────────────

    [ObservableProperty] private ApiProvider? _selectedProvider;

    [ObservableProperty] private string? _selectedModelId;

    [ObservableProperty] private Conversation? _selectedConversation;

    [ObservableProperty] private Skill? _selectedSkill;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(SendOrStopCommand))]
    private string _draft = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(SendOrStopCommand))]
    private bool _isSending;

    // ── Derived chat properties ──────────────────────────────────────────

    public IReadOnlyList<string> AvailableModels => SelectedProvider?.ModelIds ?? [];
    public LocalizationService Localization => _localization;
    public string SelectedProviderDisplayName => SelectedProvider?.Name ?? T("SelectApiKey");
    public string SelectedModelDisplayName => SelectedModelId ?? SelectedProvider?.ModelId ?? T("ModelPlaceholder");
    public string SendButtonText => IsSending ? T("Stop") : T("Send");
    public MaterialIconKind SendButtonIcon => IsSending ? MaterialIconKind.Stop : MaterialIconKind.Send;

    public bool ShowAddAttachmentButton =>
        SelectedProvider is not null && SelectedModelId is not null && SelectedProvider.MultimodalModelIds.Contains(SelectedModelId);

    public bool HasWorkspaceEntries => WorkspaceEntries.Count > 0;

    /// <summary>Called by ChatWindow code-behind after DataContext is set, to provide the file picker.</summary>
    public void AttachStorageProvider(IStorageProvider? storageProvider) => _storageProvider = storageProvider;


    // ── Shortcuts (shared with global hotkey system) ─────────────────────

    [ObservableProperty] private string _chatShortcut = AppSettingsService.DefaultChatShortcut;

    [ObservableProperty] private string _newChatWindowShortcut = AppSettingsService.DefaultNewChatWindowShortcut;

    // ── Chat actions ─────────────────────────────────────────────────────

    [RelayCommand]
    private void NewConversation()
    {
        _conversation = null;
        SelectedConversation = null;
        Messages.Clear();
        ClearWorkspace();
        ResetSessionToolSelection();
        SelectedSkill = AppState.Skills.FirstOrDefault();
    }

    /// <summary>
    /// Prepares a fresh, empty session for a newly opened chat window, defaulting the
    /// selected skill to the first available one. If the skills list is not loaded yet,
    /// the skills collection change handler completes the default selection once loaded.
    /// </summary>
    public void PrepareNewSession()
    {
        _conversation = null;
        SelectedConversation = null;
        Messages.Clear();
        ClearWorkspace();
        ResetSessionToolSelection();
        if (AppState.Skills.Count > 0)
        {
            SelectedSkill = AppState.Skills[0];
        }

        if (AppState.Providers.Count > 0 && SelectedProvider == null)
        {
            SelectedProvider = AppState.Providers[0];
        }
    }

    private void ResetConversation()
    {
        _conversation = null;
        SelectedConversation = null;
        Messages.Clear();
        ClearWorkspace();
        ResetSessionToolSelection();
    }

    // ── Attachment management ─────────────────────────────────────────────

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        if (_storageProvider is null) return;
        var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = T("AddImage"),
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp"] }]
        });
        var paths = files.Select(file => file.TryGetLocalPath()).Where(path => !string.IsNullOrWhiteSpace(path)).Cast<string>().ToList();
        if (paths.Count > 0) await AddAttachmentsAsync(paths);
    }

    /// <summary>Copies image files into the app attachments directory and exposes them as attachments.</summary>
    public async Task AddAttachmentsAsync(IEnumerable<string> sourcePaths)
    {
        foreach (var source in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) continue;
            var mimeType = ImageMimeTypeFromExtension(Path.GetExtension(source));
            if (mimeType is null) continue;
            var destination = Path.Combine(_appPaths.AttachmentsDirectory, $"{Guid.NewGuid():N}{Path.GetExtension(source).ToLowerInvariant()}");
            Directory.CreateDirectory(_appPaths.AttachmentsDirectory);
            File.Copy(source, destination, overwrite: false);
            Attachments.Add(new AttachmentViewModel(destination, Path.GetFileName(source), mimeType));
        }
    }

    /// <summary>
    /// Saves an image obtained from the system clipboard and adds it to the draft.
    /// Clipboard bitmaps are encoded as PNG by the view before reaching this method.
    /// </summary>
    public async Task AddClipboardImageAsync(Stream imageStream)
    {
        ArgumentNullException.ThrowIfNull(imageStream);

        var destination = Path.Combine(_appPaths.AttachmentsDirectory, $"{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(_appPaths.AttachmentsDirectory);

        try
        {
            await using var destinationStream = File.Create(destination);
            await imageStream.CopyToAsync(destinationStream);
            Attachments.Add(new AttachmentViewModel(destination, "clipboard.png", "image/png"));
        }
        catch
        {
            TryDeleteFile(destination);
            throw;
        }
    }

    [RelayCommand]
    private void RemoveAttachment(AttachmentViewModel? attachment)
    {
        if (attachment is null || !Attachments.Remove(attachment)) return;
        if (!attachment.IsPersisted) TryDeleteFile(attachment.FilePath);
    }

    private static string? ImageMimeTypeFromExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => null
    };

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }

    public bool HasAttachments => Attachments.Count > 0;

    private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SendCommand.NotifyCanExecuteChanged();
        SendOrStopCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowAddAttachmentButton));
        OnPropertyChanged(nameof(HasAttachments));
    }

    private void ClearUnsentAttachments()
    {
        foreach (var attachment in Attachments.ToList())
        {
            Attachments.Remove(attachment);
            if (!attachment.IsPersisted) TryDeleteFile(attachment.FilePath);
        }
    }

    // ── Workspace management ───────────────────────────────────────────────

    [RelayCommand]
    private async Task AddWorkspaceFilesAsync()
    {
        if (_storageProvider is null) return;
        try
        {
            var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = T("SelectWorkspaceFiles"),
                AllowMultiple = true
            });
            AddWorkspaceEntries(files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => new WorkspaceEntry(path!, IsDirectory: false)));
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ChatWindowViewModel), "Failed to select workspace files");
        }
    }

    [RelayCommand]
    private async Task AddWorkspaceFoldersAsync()
    {
        if (_storageProvider is null) return;
        try
        {
            var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = T("SelectWorkspaceFolders"),
                AllowMultiple = true
            });
            AddWorkspaceEntries(folders
                .Select(folder => folder.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => new WorkspaceEntry(path!, IsDirectory: true)));
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ChatWindowViewModel), "Failed to select workspace folders");
        }
    }

    public void AddWorkspaceEntries(IEnumerable<WorkspaceEntry> entries)
    {
        _workspace.Replace(_workspace.Entries.Concat(entries));
        RefreshWorkspaceEntries();
    }

    [RelayCommand]
    private void RemoveWorkspaceEntry(WorkspaceEntryViewModel? entry)
    {
        if (entry is null) return;
        _workspace.Replace(_workspace.Entries.Where(item =>
            !string.Equals(item.Path, entry.Path,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)));
        RefreshWorkspaceEntries();
    }

    [RelayCommand]
    private void OpenWorkspaceEntry(WorkspaceEntryViewModel? entry)
    {
        if (entry is null) return;

        try
        {
            var path = entry.Path;
            if (!File.Exists(path) && !Directory.Exists(path))
                throw new FileNotFoundException("The workspace entry no longer exists.", path);

            if (OperatingSystem.IsWindows())
            {
                var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                if (entry.IsFile)
                    startInfo.Arguments = $"/select,\"{path}\"";
                else
                    startInfo.ArgumentList.Add(path);
                Process.Start(startInfo);
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                var startInfo = new ProcessStartInfo("open") { UseShellExecute = false };
                if (entry.IsFile) startInfo.ArgumentList.Add("-R");
                startInfo.ArgumentList.Add(path);
                Process.Start(startInfo);
                return;
            }

            var directory = entry.IsDirectory ? path : Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Process.Start(new ProcessStartInfo("xdg-open")
                {
                    UseShellExecute = false,
                    ArgumentList = { directory }
                });
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ChatWindowViewModel), "Failed to open workspace entry");
        }
    }

    private void RefreshWorkspaceEntries()
    {
        WorkspaceEntries.Clear();
        foreach (var entry in _workspace.Entries)
        {
            WorkspaceEntries.Add(new WorkspaceEntryViewModel(entry.Path, entry.IsDirectory));
        }

        OnPropertyChanged(nameof(HasWorkspaceEntries));
    }

    private void ClearWorkspace()
    {
        _workspace.Clear();
        RefreshWorkspaceEntries();
    }

    // ── Send / stop ──────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (SelectedProvider is null)
        {
            return;
        }

        var text = Draft.Trim();
        if (text.Length == 0 && Attachments.Count == 0) return;

        var selectedProvider = SelectedProvider with { ModelId = SelectedModelId ?? SelectedProvider.ModelId };
        // Resolve the selected prompt from the shared collection at send time.
        // Settings can replace immutable Skill records while this window is open;
        // using the live record prevents a stale object from being snapshotted.
        var selectedSkill = SelectedSkill is { } currentSkill
            ? AppState.Skills.FirstOrDefault(skill => skill.Id == currentSkill.Id) ?? currentSkill
            : null;
        _conversation ??= await _conversations.CreateAsync(selectedProvider, selectedSkill);
        if (AppState.Conversations.All(item => item.Id != _conversation.Id))
            AppState.Conversations.Insert(0, _conversation);

        var attachments = Attachments.Select(a => new MessageAttachment(a.FilePath, a.FileName, a.MimeType)).ToList();
        foreach (var a in Attachments) a.IsPersisted = true;
        Draft = string.Empty;
        // The outgoing message owns this attachment snapshot. Clear the draft now so
        // its previews disappear immediately and new attachments can be selected for
        // the next message while a response is still streaming.
        Attachments.Clear();
        var assistant = new ChatMessageViewModel(MessageRole.Assistant, string.Empty);
        Messages.Add(new ChatMessageViewModel(MessageRole.User, text, attachments));
        Messages.Add(assistant);
        ScrollMessagesToEndRequested?.Invoke(this, EventArgs.Empty);
        _sendCancellation = new CancellationTokenSource();
        IsSending = true;

        var channel = Channel.CreateBounded<ChatStreamUpdate>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        var producerTask = Task.Run(async () =>
        {
            try
            {
                var selectedTools = SelectedToolNames;
                await foreach (var update in _chat.SendStreamingAsync(_conversation.Id, text, attachments, selectedTools, _sendCancellation.Token)
                                   .ConfigureAwait(false))
                    await channel.Writer.WriteAsync(update, _sendCancellation.Token).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException)
            {
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, _sendCancellation.Token);

        try
        {
            await foreach (var update in channel.Reader.ReadAllAsync(_sendCancellation.Token))
            {
                if (update.Kind == ChatStreamUpdateKind.Reasoning && !string.IsNullOrEmpty(update.Content))
                {
                    assistant.AppendReasoning(update.Content, T("Thinking"));
                    continue;
                }

                if (update.Kind == ChatStreamUpdateKind.ToolStarted &&
                    update.Content is { Length: > 0 } notice)
                {
                    assistant.AppendToolCall(notice, T("CallingTools"));
                    continue;
                }

                if (update.Kind == ChatStreamUpdateKind.ToolCompleted)
                {
                    assistant.CompleteThinking();
                    continue;
                }

                if (update.Kind != ChatStreamUpdateKind.Text || string.IsNullOrEmpty(update.Content))
                {
                    continue;
                }

                assistant.AppendText(update.Content);
            }

            await producerTask;
            await RefreshConversationHistoryAsync();
        }
        catch (OperationCanceledException exception)
        {
            ExceptionLogger.Log(exception, nameof(ChatWindowViewModel), "Chat request cancelled", LogLevel.Information);
            if (string.IsNullOrWhiteSpace(assistant.Content))
                assistant.Content = "The response was cancelled.";
            await RefreshConversationHistoryAsync();
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ChatWindowViewModel), "Chat request failed");
            assistant.Content =
                string.IsNullOrWhiteSpace(assistant.Content) ? $"Error: {exception.Message}" : assistant.Content;
            await RefreshConversationHistoryAsync();
        }
        finally
        {
            assistant.CompleteResponse();
            _sendCancellation.Dispose();
            _sendCancellation = null;
            IsSending = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _sendCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanSendOrStop))]
    private void SendOrStop()
    {
        if (IsSending)
        {
            Stop();
            return;
        }

        _ = SendAsync();
    }

    private bool CanSend() => !IsSending && (!string.IsNullOrWhiteSpace(Draft) || Attachments.Count > 0);
    private bool CanStop() => IsSending;
    private bool CanSendOrStop() => IsSending || !string.IsNullOrWhiteSpace(Draft) || Attachments.Count > 0;

    private void RebuildSessionTools()
    {
        var hadSessionTools = SessionTools.Count > 0;
        var selectedToolNames = SessionTools.Where(static tool => tool.IsSelected)
            .Select(static tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var tool in SessionTools)
            tool.PropertyChanged -= OnSessionToolPropertyChanged;
        SessionTools.Clear();
        foreach (var tool in AppState.AvailableTools)
        {
            var selection = new SessionToolSelection(
                tool.Name,
                tool.DisplayName,
                tool.Description,
                !hadSessionTools || selectedToolNames.Contains(tool.Name));
            selection.PropertyChanged += OnSessionToolPropertyChanged;
            SessionTools.Add(selection);
        }
        OnPropertyChanged(nameof(HasSessionTools));
        OnPropertyChanged(nameof(SelectedToolNames));
    }

    private void ResetSessionToolSelection()
    {
        foreach (var tool in SessionTools)
            tool.IsSelected = true;
    }

    private void OnToolsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RebuildSessionTools);

    private void OnAppToolPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ToolAvailability.IsEnabled)
            or nameof(ToolAvailability.DisplayName)
            or nameof(ToolAvailability.Description))
            Dispatcher.UIThread.Post(RebuildSessionTools);
    }

    private void OnSessionToolPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionToolSelection.IsSelected))
            OnPropertyChanged(nameof(SelectedToolNames));
    }

    // ── Navigation ───────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenSettings() => _navigation?.ShowSettings();

    [RelayCommand]
    private void OpenSkillWorkbench() => _navigation?.ShowSkillSettings();

    [RelayCommand]
    private void ShowChat() => _chatWindowManager?.Show();

    // ── Shortcut helpers ─────────────────────────────────────────────────

    public bool IsChatShortcut(Key key, KeyModifiers modifiers) =>
        ShortcutFormatter.Matches(ChatShortcut, key, modifiers);

    public void UpdateGlobalShortcuts() =>
        _globalHotKeys?.UpdateShortcuts(ChatShortcut, NewChatWindowShortcut);

    partial void OnChatShortcutChanged(string value) => UpdateGlobalShortcuts();

    partial void OnNewChatWindowShortcutChanged(string value) => UpdateGlobalShortcuts();

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppState.ChatShortcut):
                ChatShortcut = AppState.ChatShortcut;
                break;
            case nameof(AppState.NewChatWindowShortcut):
                NewChatWindowShortcut = AppState.NewChatWindowShortcut;
                break;
        }
    }

    // ── Conversation lifecycle ───────────────────────────────────────────

    public async Task OpenConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);
        if (conversation is null) return;
        if (_conversation?.Id != conversation.Id) ClearWorkspace();
        if (AppState.Conversations.All(item => item.Id != conversation.Id))
            AppState.Conversations.Insert(0, conversation);
        SelectedConversation = conversation;
    }

    private async Task RefreshConversationHistoryAsync(CancellationToken cancellationToken = default)
    {
        var selectedId = SelectedConversation?.Id;
        await AppState.RefreshConversationHistoryAsync(cancellationToken);
        if (selectedId is not null)
            SelectedConversation = AppState.Conversations.FirstOrDefault(c => c.Id == selectedId);
    }

    private async Task OpenConversationAsync(Conversation conversation)
    {
        _openingConversation = true;
        try
        {
            if (_conversation?.Id != conversation.Id)
            {
                ClearWorkspace();
                ResetSessionToolSelection();
            }
            _conversation = conversation;
            var provider = AppState.Providers.FirstOrDefault(item => item.Id == conversation.ProviderId);
            if (provider is not null) SelectedProvider = provider;

            var skill = AppState.Skills.FirstOrDefault(item => item.Id == conversation.SkillId);
            if (skill is not null) SelectedSkill = skill;

            using var snapshot = JsonDocument.Parse(conversation.ProviderConfiguration);
            if (snapshot.RootElement.TryGetProperty("ModelId", out var modelId))
                SelectedModelId = modelId.GetString();

            Messages.Clear();
            foreach (var message in await _messageRepository.GetByConversationAsync(conversation.Id)
                         .ConfigureAwait(false))
                Messages.Add(new ChatMessageViewModel(message.Role, message.Content, message.Attachments));

        }
        finally
        {
            _openingConversation = false;
        }
    }

    // ── Partial change handlers ──────────────────────────────────────────

    partial void OnIsSendingChanged(bool value)
    {
        OnPropertyChanged(nameof(SendButtonText));
        OnPropertyChanged(nameof(SendButtonIcon));
        StopCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProviderChanged(ApiProvider? value)
    {
        OnPropertyChanged(nameof(AvailableModels));
        OnPropertyChanged(nameof(SelectedProviderDisplayName));
        OnPropertyChanged(nameof(SelectedModelDisplayName));
        if (!_openingConversation) ResetConversation();
        if (value is not null) SelectedModelId = value.ModelId;
        OnPropertyChanged(nameof(ShowAddAttachmentButton));
        if (!ShowAddAttachmentButton) ClearUnsentAttachments();
    }

    partial void OnSelectedModelIdChanged(string? value)
    {
        OnPropertyChanged(nameof(SelectedModelDisplayName));
        if (!_openingConversation && !string.IsNullOrWhiteSpace(value)) ResetConversation();
        OnPropertyChanged(nameof(ShowAddAttachmentButton));
        if (!ShowAddAttachmentButton) ClearUnsentAttachments();
    }

    partial void OnSelectedSkillChanged(Skill? value)
    {
        if (value is null)
        {
            // The selection can briefly become null while AppState replaces the
            // collection after a prompt is edited. The posted restore below will
            // select the replacement object once the collection is populated.
            if (!_restoringSkillSelection)
                RestoreSkillSelection();
            return;
        }

        var previousId = _lastSelectedSkillId;
        _lastSelectedSkillId = value.Id;

        // A user changing prompts starts a fresh conversation, as it did before
        // the shared skills list was made reloadable. Replacing the object for the
        // same prompt after an edit must not reset the active conversation.
        if (!_openingConversation && !_restoringSkillSelection &&
            previousId is not null && !string.Equals(previousId, value.Id, StringComparison.Ordinal))
        {
            ResetConversation();
        }
    }

    /// <summary>
    /// Re-validates <see cref="SelectedSkill"/> after the shared <see cref="AppState.Skills"/>
    /// list changes. The current conversation is left untouched; only the dropdown
    /// selection is repaired — preferring the previously selected skill and falling
    /// back to the first available skill if it was deleted.
    /// </summary>
    private void OnSkillsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // The skills list is mutated from a worker thread during a reload, so
        // repair the selection back on the UI thread (idempotent + re-runs on
        // the final state after the last collection change).
        Dispatcher.UIThread.Post(RestoreSkillSelection);
    }

    private void RestoreSkillSelection()
    {
        if (AppState.Skills.Count == 0)
        {
            return;
        }

        var next = _lastSelectedSkillId is null
            ? AppState.Skills[0]
            : AppState.Skills.FirstOrDefault(skill => skill.Id == _lastSelectedSkillId) ?? AppState.Skills[0];

        // AppState reloads records as new immutable objects. Compare by reference
        // rather than ID so an edited prompt replaces the stale selected object
        // even though its identity is unchanged.
        if (ReferenceEquals(SelectedSkill, next))
        {
            return;
        }

        _restoringSkillSelection = true;
        try
        {
            SelectedSkill = next;
        }
        finally
        {
            _restoringSkillSelection = false;
        }
    }

    partial void OnSelectedConversationChanged(Conversation? value)
    {
        if (value is not null) _ = OpenConversationAsync(value);
    }

    public void SelectModel(ApiProvider provider, string model)
    {
        SelectedProvider = provider;
        SelectedModelId = model;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private string T(string key) => _localization[key];

    public override void Dispose()
    {
        AppState.Skills.CollectionChanged -= OnSkillsCollectionChanged;
        AppState.Tools.CollectionChanged -= OnToolsCollectionChanged;
        foreach (var tool in AppState.Tools)
            tool.PropertyChanged -= OnAppToolPropertyChanged;
        foreach (var tool in SessionTools)
            tool.PropertyChanged -= OnSessionToolPropertyChanged;
        AppState.PropertyChanged -= OnAppStatePropertyChanged;
        _sendCancellation?.Cancel();
        _sendCancellation?.Dispose();
        _sendCancellation = null;

        Messages.Clear();
        ClearWorkspace();

        _conversation = null;
        _openingConversation = false;
        base.Dispose();
    }
}
