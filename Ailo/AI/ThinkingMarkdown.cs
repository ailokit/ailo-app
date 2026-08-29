using System.Text;

namespace Ailo.AI;

/// <summary>Stores transient model thinking and tool notices as ordered Markdown blocks.</summary>
public static class ThinkingMarkdown
{
    private const string OpeningFence = "````thinking\n";
    private const string ClosingFence = "````\n";
    private const string ToolMarker = "<!-- ailo-tool -->\n";
    private const string ToolEndMarker = "<!-- /ailo-tool -->\n";

    /// <summary>Appends a new thinking block, preserving its position relative to normal text.</summary>
    public static string AppendBlock(string content, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return content;
        }

        // A fenced block must begin on its own line. Without this separator, a tool or
        // reasoning block emitted after normal text is parsed as part of that paragraph.
        var prefix = content.Length > 0 && content[^1] != '\n'
            ? content + "\n"
            : content;
        return prefix + OpeningFence + text + "\n" + ClosingFence;
    }

    /// <summary>Appends a tool notice to the current thinking block, creating one only after normal text.</summary>
    public static string AppendToolBlock(string content, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return content;
        }

        if (TryGetLastBlock(content, out var bodyStart, out _, out var closingIndex) &&
            IsWhitespaceOnly(content, closingIndex + ClosingFence.Length))
        {
            var insertIndex = GetInsertIndex(content, closingIndex);
            var separator = insertIndex > bodyStart && content[insertIndex - 1] != '\n' ? "\n" : string.Empty;
            return content.Insert(insertIndex, separator + ToolMarker + text + "\n" + ToolEndMarker);
        }

        return AppendBlock(content, ToolMarker + text + "\n" + ToolEndMarker);
    }

    /// <summary>Repairs fences written by older builds before they are sent to the Markdown renderer.</summary>
    public static string NormalizeForRendering(string content)
    {
        var firstFence = content.IndexOf(OpeningFence, StringComparison.Ordinal);
        if (firstFence <= 0)
        {
            return content;
        }

        var result = new StringBuilder(content.Length + 4);
        var cursor = 0;
        while (firstFence >= 0)
        {
            result.Append(content, cursor, firstFence - cursor);
            if (result.Length > 0 && result[^1] != '\n')
            {
                result.Append('\n');
            }

            result.Append(OpeningFence);
            cursor = firstFence + OpeningFence.Length;
            firstFence = content.IndexOf(OpeningFence, cursor, StringComparison.Ordinal);
        }

        result.Append(content, cursor, content.Length - cursor);
        return result.ToString();
    }

    /// <summary>Continues the last thinking block when it is the current stream segment.</summary>
    public static string AppendReasoning(string content, string reasoning)
    {
        if (string.IsNullOrEmpty(reasoning))
        {
            return content;
        }

        if (TryGetLastBlock(content, out var bodyStart, out var bodyLength, out var closingIndex) &&
            IsWhitespaceOnly(content, closingIndex + ClosingFence.Length))
        {
            // Streaming reasoning chunks already contain the provider's whitespace.
            // Insert before the fence's terminator newline; adding a newline per chunk
            // turns every token-sized update into a new line.
            var insertIndex = GetInsertIndex(content, closingIndex);
            var separator = content.AsSpan(bodyStart, bodyLength).EndsWith(ToolEndMarker.AsSpan(), StringComparison.Ordinal)
                ? "\n"
                : string.Empty;
            return content.Insert(insertIndex, separator + reasoning);
        }

        return AppendBlock(content, reasoning);
    }

    public static bool ContainsReasoning(string content)
    {
        var cursor = 0;
        while (cursor < content.Length)
        {
            var openingIndex = content.IndexOf(OpeningFence, cursor, StringComparison.Ordinal);
            if (openingIndex < 0)
            {
                return false;
            }

            var bodyStart = openingIndex + OpeningFence.Length;
            var closingIndex = content.IndexOf(ClosingFence, bodyStart, StringComparison.Ordinal);
            if (closingIndex < 0)
            {
                return false;
            }

            var bodyLength = closingIndex - bodyStart;
            if (bodyLength > 0 && content[closingIndex - 1] == '\n')
            {
                bodyLength--;
            }

            if (!string.IsNullOrWhiteSpace(content.Substring(bodyStart, bodyLength)))
            {
                return true;
            }

            cursor = closingIndex + ClosingFence.Length;
        }

        return false;
    }

    /// <summary>Removes all UI-only thinking blocks before an agent session is sent back to a model.</summary>
    public static string RemoveThinkingBlocks(string content)
    {
        var result = new StringBuilder(content.Length);
        var cursor = 0;
        while (cursor < content.Length)
        {
            var openingIndex = content.IndexOf(OpeningFence, cursor, StringComparison.Ordinal);
            if (openingIndex < 0)
            {
                result.Append(content, cursor, content.Length - cursor);
                break;
            }

            result.Append(content, cursor, openingIndex - cursor);
            var bodyStart = openingIndex + OpeningFence.Length;
            var closingIndex = content.IndexOf(ClosingFence, bodyStart, StringComparison.Ordinal);
            if (closingIndex < 0)
            {
                // Keep malformed content rather than silently dropping a partial stream.
                result.Append(content, openingIndex, content.Length - openingIndex);
                break;
            }

            cursor = closingIndex + ClosingFence.Length;
            if (cursor < content.Length && result.Length > 0 &&
                !char.IsWhiteSpace(result[^1]) && !char.IsWhiteSpace(content[cursor]))
            {
                result.Append('\n');
            }
        }

        return result.ToString();
    }

    private static bool TryGetLastBlock(string content, out int bodyStart, out int bodyLength, out int closingIndex)
    {
        var openingIndex = content.LastIndexOf(OpeningFence, StringComparison.Ordinal);
        if (openingIndex < 0)
        {
            bodyStart = 0;
            bodyLength = 0;
            closingIndex = 0;
            return false;
        }

        bodyStart = openingIndex + OpeningFence.Length;
        closingIndex = content.IndexOf(ClosingFence, bodyStart, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            bodyLength = 0;
            return false;
        }

        bodyLength = closingIndex - bodyStart;
        if (bodyLength > 0 && content[closingIndex - 1] == '\n')
        {
            bodyLength--;
        }

        return true;
    }

    private static bool IsWhitespaceOnly(string content, int start)
    {
        for (var index = start; index < content.Length; index++)
        {
            if (!char.IsWhiteSpace(content[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetInsertIndex(string content, int closingIndex) =>
        closingIndex > 0 && content[closingIndex - 1] == '\n' ? closingIndex - 1 : closingIndex;
}
