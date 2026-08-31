using LiveMarkdown.Avalonia;
using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;

namespace Ailo.Views;

/// <summary>Markdig block type used exclusively for Ailo's UI-only thinking fences.</summary>
public sealed class ThinkingCodeBlock(BlockParser parser) : FencedCodeBlock(parser);

/// <summary>
/// Parses only <c>thinking</c> fences into <see cref="ThinkingCodeBlock"/>.
/// Other fenced blocks are left for Markdig's built-in parser and therefore retain
/// LiveMarkdown's regular code-block renderer.
/// </summary>
public sealed class ThinkingBlockParser : FencedBlockParserBase<ThinkingCodeBlock>
{
    public ThinkingBlockParser()
    {
        OpeningCharacters = ['`', '~'];
        InfoPrefix = "thinking";
    }

    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (!IsThinkingFenceOpening(processor.Line.AsSpan()))
        {
            return BlockState.None;
        }

        return base.TryOpen(processor);
    }

    protected override ThinkingCodeBlock CreateFencedBlock(BlockProcessor processor) => new(this);

    private static bool IsThinkingFenceOpening(ReadOnlySpan<char> line)
    {
        line = line.TrimStart();
        if (line.Length < 4 || line[0] is not ('`' or '~'))
        {
            return false;
        }

        var fence = line[0];
        var index = 0;
        while (index < line.Length && line[index] == fence)
        {
            index++;
        }

        if (index < 3)
        {
            return false;
        }

        var info = line[index..].TrimStart();
        var tokenEnd = 0;
        while (tokenEnd < info.Length && !char.IsWhiteSpace(info[tokenEnd]))
        {
            tokenEnd++;
        }

        return tokenEnd > 0 && info[..tokenEnd].Equals("thinking", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Adds the parser that promotes <c>thinking</c> fences to their own Markdig block type.</summary>
public sealed class ThinkingMarkdownExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.BlockParsers.Contains<ThinkingBlockParser>())
        {
            pipeline.BlockParsers.Insert(0, new ThinkingBlockParser());
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }
}

public static class ThinkingMarkdownExtensionBuilder
{
    public static MarkdownPipelineBuilder UseThinkingBlocks(this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<ThinkingMarkdownExtension>();
        return pipeline;
    }
}
