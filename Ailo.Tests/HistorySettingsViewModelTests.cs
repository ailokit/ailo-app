using Ailo.AI.Conversations;
using Ailo.ViewModels;

namespace Ailo.Tests;

public sealed class HistorySettingsViewModelTests
{
    [Fact]
    public void CreateHistoryMessage_PreservesPersistedImageAttachments()
    {
        var attachment = new MessageAttachment("/tmp/photo.png", "photo.png", "image/png");
        var message = new Message(
            "message",
            "conversation",
            1,
            MessageRole.User,
            "Look at this",
            MessageStatus.Completed,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow)
        {
            Attachments = [attachment]
        };

        var historyMessage = HistorySettingsViewModel.CreateHistoryMessage(message);

        Assert.True(historyMessage.HasAttachments);
        Assert.Equal(attachment, Assert.Single(historyMessage.Attachments));
    }
}
