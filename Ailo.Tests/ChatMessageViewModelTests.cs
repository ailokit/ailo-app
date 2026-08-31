using Ailo.AI.Conversations;
using Ailo.AI;
using Ailo.ViewModels;
using Ailo.Views;
using LiveMarkdown.Avalonia;
using Markdig;
using Markdig.Syntax;

namespace Ailo.Tests;

public sealed class ChatMessageViewModelTests
{
    [Fact]
    public void StreamingResponseBuffer_CoalescesLongTokenStreamsWithoutLosingContent()
    {
        var buffer = new StreamingResponseBuffer();
        var updateCount = 0;
        for (var index = 0; index < 5_000; index++)
        {
            buffer.AppendReasoning("r");
            if (buffer.ShouldFlush)
            {
                updateCount += buffer.Drain().Updates.Count;
            }
        }

        buffer.AppendText("Answer");
        for (var index = 0; index < 5_000; index++)
        {
            buffer.AppendText("t");
            if (buffer.ShouldFlush)
            {
                updateCount += buffer.Drain().Updates.Count;
            }
        }

        var (content, updates) = buffer.Drain();

        Assert.Contains("````thinking\n" + new string('r', 5_000), content);
        Assert.EndsWith("Answer" + new string('t', 5_000), content);
        Assert.Equal(6, updateCount + updates.Count);
        Assert.Single(updates);
        Assert.Equal(ChatStreamUpdateKind.Text, updates[0].Kind);
    }

    [Fact]
    public void ThinkingFence_UsesItsOwnMarkdigBlockType_WhileCodeFencesRemainRegularCodeBlocks()
    {
        const string markdown = "````thinking\nAnalyze the request\n````\n```java\nclass Example {}\n```\n";
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseCodeBlockSpanFixer()
            .UseThinkingBlocks()
            .Build();
        var parsed = Markdown.Parse(markdown, pipeline);

        Assert.IsType<ThinkingCodeBlock>(parsed[0]);
        Assert.IsType<FencedCodeBlock>(parsed[1]);
        Assert.IsNotType<ThinkingCodeBlock>(parsed[1]);
        Assert.Equal("java", ((FencedCodeBlock)parsed[1]).Info?.ToString().Trim());

        var nodeBaseType = typeof(ThinkingBlockNode).BaseType;
        Assert.NotNull(nodeBaseType);
        Assert.Equal(typeof(ThinkingCodeBlock), Assert.Single(nodeBaseType.GetGenericArguments()));

        var indicatorField = typeof(ThinkingBlockControl).GetField(
            "_indicator",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(indicatorField);
        Assert.Equal(typeof(Material.Icons.Avalonia.MaterialIcon), indicatorField.FieldType);
    }

    [Fact]
    public void LegacyThinkingFence_IsMovedToItsOwnLineBeforeRendering()
    {
        var content = "Normal text.````thinking\nAnalyze the request\n````\n";

        Assert.Equal("Normal text.\n````thinking\nAnalyze the request\n````\n", ThinkingMarkdown.NormalizeForRendering(content));
    }

    [Fact]
    public void ThinkingMarkdown_IsStoredInsideTheAssistantContent()
    {
        var content = ThinkingMarkdown.AppendReasoning("Answer", "Analyze the request first");

        Assert.Equal("Answer\n````thinking\nAnalyze the request first\n````\n", content);
    }

    [Fact]
    public void Reasoning_IsExpandedWhileStreamingAndCollapsedWhenResponseCompletes()
    {
        var message = new ChatMessageViewModel(MessageRole.Assistant, string.Empty);

        message.AppendReasoning("Analyze the request first", "Thinking");

        Assert.True(message.HasReasoning);
        Assert.Equal("Thinking", message.ThinkingStatus);

        message.CompleteResponse();

        Assert.Null(message.ThinkingStatus);
        Assert.Contains("Analyze the request first", message.Content);
    }

    [Fact]
    public void PersistedReasoningStartsCollapsedAndCanBeExpandedAgain()
    {
        var message = new ChatMessageViewModel(
            MessageRole.Assistant,
            ThinkingMarkdown.AppendReasoning("Answer", "Historical reasoning"));

        Assert.True(message.HasReasoning);
        Assert.True(message.HasReasoning);
    }

    [Fact]
    public void ThinkingAndToolBlocksKeepGenerationOrder()
    {
        var message = new ChatMessageViewModel(MessageRole.Assistant, string.Empty);

        message.AppendReasoning("Reason first", "Thinking");
        message.AppendText("Initial conclusion");
        message.AppendToolCall("Calling tool: read workspace", "Calling tool");
        message.AppendText("Response after tool result");

        var thinkingIndex = message.Content.IndexOf("Reason first", StringComparison.Ordinal);
        var firstTextIndex = message.Content.IndexOf("Initial conclusion", StringComparison.Ordinal);
        var toolIndex = message.Content.IndexOf("Calling tool: read workspace", StringComparison.Ordinal);
        var finalTextIndex = message.Content.IndexOf("Response after tool result", StringComparison.Ordinal);

        Assert.True(thinkingIndex < firstTextIndex);
        Assert.True(firstTextIndex < toolIndex);
        Assert.True(toolIndex < finalTextIndex);
    }

    [Fact]
    public void ThinkingBlocksCanBeRemovedBeforeBuildingModelContext()
    {
        var content = "Question" + ThinkingMarkdown.AppendBlock(string.Empty, "Thinking") + "Answer";

        Assert.Equal("Question\nAnswer", ThinkingMarkdown.RemoveThinkingBlocks(content));
    }

    [Fact]
    public void StreamingReasoningChunksDoNotCreateArtificialLineBreaks()
    {
        var content = ThinkingMarkdown.AppendReasoning(string.Empty, "One ");
        content = ThinkingMarkdown.AppendReasoning(content, "continuous sentence");

        Assert.Contains("One continuous sentence", content);
        Assert.DoesNotContain("One \ncontinuous sentence", content);
    }

    [Fact]
    public void ContinuousReasoningAfterTrailingWhitespaceUsesOneThinkingBlock()
    {
        var content = ThinkingMarkdown.AppendReasoning(string.Empty, "First reasoning segment") + "\n";
        content = ThinkingMarkdown.AppendReasoning(content, "Second reasoning segment");

        Assert.Equal(1, content.Split("````thinking", StringSplitOptions.None).Length - 1);
        Assert.Contains("First reasoning segmentSecond reasoning segment", content);
    }

    [Fact]
    public void ReasoningAndToolCallsStayInOneBlockUntilNormalTextArrives()
    {
        var content = ThinkingMarkdown.AppendReasoning(string.Empty, "Think first");
        content = ThinkingMarkdown.AppendToolBlock(content, "MCP: weather / get_weather");
        content = ThinkingMarkdown.AppendReasoning(content, "Think again");

        Assert.True(content.IndexOf("Think first", StringComparison.Ordinal) < content.IndexOf("MCP:", StringComparison.Ordinal));
        Assert.True(content.IndexOf("MCP:", StringComparison.Ordinal) < content.IndexOf("Think again", StringComparison.Ordinal));
        Assert.Equal(1, content.Split("````thinking", StringSplitOptions.None).Length - 1);

        content += "Normal text";
        content = ThinkingMarkdown.AppendReasoning(content, "New reasoning");
        Assert.Equal(2, content.Split("````thinking", StringSplitOptions.None).Length - 1);
    }
}
