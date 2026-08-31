using Ailo.AI;
using Ailo.AI.Tools;
using Ailo.Services;
using Microsoft.Extensions.AI;

namespace Ailo.Tests;

public sealed class SystemNotificationToolTests
{
    [Fact]
    public async Task SendsImmediateNativeNotification()
    {
        var notifications = new FakeNotificationService();
        var tool = new SystemNotificationTool(notifications);

        var result = await tool.ShowNotificationAsync("Build complete", "All checks passed", "Ailo", NotificationType.TopmostWindow);

        Assert.Equal((NotificationType.TopmostWindow, "Build complete", "All checks passed", "Ailo"), notifications.LastNotification);
        Assert.Contains("Build complete", result);
    }

    [Fact]
    public async Task RejectsBlankNotificationContent()
    {
        var tool = new SystemNotificationTool(new FakeNotificationService());

        await Assert.ThrowsAsync<ArgumentException>(() => tool.ShowNotificationAsync("Title", " "));
    }

    [Fact]
    public void CreatesNotificationFunctionWithSourceGeneratedEnumMetadata()
    {
        var tool = new SystemNotificationTool(new FakeNotificationService());

        var function = AIFunctionFactory.Create(
            tool.ShowNotificationAsync,
            new AIFunctionFactoryOptions
            {
                SerializerOptions = AiloJsonSerializerOptions.AgentSession
            });

        Assert.Contains("notificationType", function.JsonSchema.GetRawText());
        Assert.Contains("Native", function.JsonSchema.GetRawText());
        Assert.Contains("TopmostWindow", function.JsonSchema.GetRawText());
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public (NotificationType Type, string Title, string Body, string? Subtitle)? LastNotification { get; private set; }

        public Task ShowAsync(NotificationType type, string title, string body, string? subtitle = null, CancellationToken cancellationToken = default)
        {
            LastNotification = (type, title, body, subtitle);
            return Task.CompletedTask;
        }
    }
}
