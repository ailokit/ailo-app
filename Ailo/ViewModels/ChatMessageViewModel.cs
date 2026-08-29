using Ailo.AI;
using Ailo.AI.Conversations;

namespace Ailo.ViewModels;

public sealed partial class ChatMessageViewModel(
    MessageRole role,
    string content,
    IReadOnlyList<MessageAttachment>? attachments = null) : ViewModelBase
{
    public MessageRole Role { get; } = role;
    public bool IsUser => Role == MessageRole.User;
    public bool IsAssistant => Role == MessageRole.Assistant;
    public IReadOnlyList<MessageAttachment> Attachments { get; } = attachments ?? [];
    public bool HasAttachments => Attachments.Count > 0;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _content = content;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string? _thinkingStatus;

    public bool HasReasoning => ThinkingMarkdown.ContainsReasoning(Content);

    public void AppendReasoning(string text, string status)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Content = ThinkingMarkdown.AppendReasoning(Content, text);
        ThinkingStatus = status;
    }

    public void AppendToolCall(string text, string status)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Content = ThinkingMarkdown.AppendToolBlock(Content, text);
        ThinkingStatus = status;
    }

    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Content += text;
        ThinkingStatus = null;
    }

    public void CompleteThinking() => ThinkingStatus = null;

    public void CompleteResponse()
    {
        ThinkingStatus = null;
    }

    partial void OnContentChanged(string value) => OnPropertyChanged(nameof(HasReasoning));
}
