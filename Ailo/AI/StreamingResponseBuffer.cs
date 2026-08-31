using System.Text;

namespace Ailo.AI;

/// <summary>
/// Accumulates a streamed assistant message without rebuilding its full Markdown document for
/// every provider token. It also combines adjacent display updates so a long response cannot
/// overwhelm the UI thread or SQLite with thousands of tiny writes.
/// </summary>
internal sealed class StreamingResponseBuffer
{
    private const int FlushThresholdCharacters = 2 * 1024;
    private readonly List<Segment> _segments = [];
    private readonly List<ChatStreamUpdate> _updates = [];
    private readonly StringBuilder _pendingContent = new();
    private ChatStreamUpdateKind? _pendingContentKind;
    private int _pendingCharacterCount;
    private bool _requiresImmediateFlush;

    public bool HasPendingUpdates => _updates.Count > 0 || _pendingContent.Length > 0;

    public bool ShouldFlush => _requiresImmediateFlush || _pendingCharacterCount >= FlushThresholdCharacters;

    public void AppendReasoning(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var segment = GetOrCreateThinkingSegment();
        if (segment.EndsWithTool)
        {
            segment.Content.Append('\n');
        }

        segment.Content.Append(text);
        segment.EndsWithTool = false;
        AppendContentUpdate(ChatStreamUpdateKind.Reasoning, text);
    }

    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var segment = _segments.LastOrDefault();
        if (segment is null || segment.Kind != SegmentKind.Text)
        {
            segment = new Segment(SegmentKind.Text);
            _segments.Add(segment);
        }

        segment.Content.Append(text);
        AppendContentUpdate(ChatStreamUpdateKind.Text, text);
    }

    public void AppendToolStarted(string callId, string notice)
    {
        var segment = GetOrCreateThinkingSegment();
        if (segment.Content.Length > 0 && segment.Content[^1] != '\n')
        {
            segment.Content.Append('\n');
        }

        segment.Content.Append("<!-- ailo-tool -->\n");
        segment.Content.Append(notice);
        segment.Content.Append("\n<!-- /ailo-tool -->\n");
        segment.EndsWithTool = true;
        FlushPendingContent();
        _updates.Add(ChatStreamUpdate.ToolStarted(callId, notice));
        _requiresImmediateFlush = true;
    }

    public void AppendToolCompleted(string callId)
    {
        FlushPendingContent();
        _updates.Add(ChatStreamUpdate.ToolCompleted(callId));
        _requiresImmediateFlush = true;
    }

    /// <summary>Returns the complete persisted content and its coalesced display updates.</summary>
    public (string Content, IReadOnlyList<ChatStreamUpdate> Updates) Drain()
    {
        FlushPendingContent();
        var updates = _updates.ToArray();
        _updates.Clear();
        _pendingCharacterCount = 0;
        _requiresImmediateFlush = false;
        return (BuildContent(), updates);
    }

    private void AppendContentUpdate(ChatStreamUpdateKind kind, string text)
    {
        if (_pendingContentKind is not null && _pendingContentKind != kind)
        {
            FlushPendingContent();
        }

        _pendingContentKind = kind;
        _pendingContent.Append(text);
        _pendingCharacterCount += text.Length;
    }

    private void FlushPendingContent()
    {
        if (_pendingContentKind is not { } kind || _pendingContent.Length == 0) return;

        _updates.Add(kind == ChatStreamUpdateKind.Reasoning
            ? ChatStreamUpdate.Reasoning(_pendingContent.ToString())
            : ChatStreamUpdate.Text(_pendingContent.ToString()));
        _pendingContent.Clear();
        _pendingContentKind = null;
    }

    private Segment GetOrCreateThinkingSegment()
    {
        var segment = _segments.LastOrDefault();
        if (segment is not null && segment.Kind == SegmentKind.Thinking)
        {
            return segment;
        }

        segment = new Segment(SegmentKind.Thinking);
        _segments.Add(segment);
        return segment;
    }

    private string BuildContent()
    {
        var content = new StringBuilder();
        foreach (var segment in _segments)
        {
            if (segment.Kind == SegmentKind.Text)
            {
                content.Append(segment.Content);
                continue;
            }

            if (content.Length > 0 && content[^1] != '\n')
            {
                content.Append('\n');
            }

            content.Append("````thinking\n");
            content.Append(segment.Content);
            if (segment.Content.Length == 0 || segment.Content[^1] != '\n')
            {
                content.Append('\n');
            }

            content.Append("````\n");
        }

        return content.ToString();
    }

    private enum SegmentKind
    {
        Text,
        Thinking
    }

    private sealed class Segment(SegmentKind kind)
    {
        public SegmentKind Kind { get; } = kind;
        public StringBuilder Content { get; } = new();
        public bool EndsWithTool { get; set; }
    }
}
