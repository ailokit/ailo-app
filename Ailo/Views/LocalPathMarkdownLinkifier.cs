using System.Text.RegularExpressions;

namespace Ailo.Views;

/// <summary>Turns existing absolute local paths in plain markdown text into clickable file links.</summary>
public static partial class LocalPathMarkdownLinkifier
{
    // Deliberately skip paths already inside Markdown links, code spans, or web URLs. The captured
    // text can include spaces, then FindExistingPath selects the longest path that actually exists.
    [GeneratedRegex("(?<path>(?:[A-Za-z]:[\\\\/]|/)[^\\r\\n`<>\\[\\]()]*)")]
    private static partial Regex LocalPathCandidateRegex();

    [GeneratedRegex("`(?<path>(?:[A-Za-z]:[\\\\/]|/)[^\\r\\n`<>\\[\\]()]+)`")]
    private static partial Regex InlineCodePathRegex();

    public static string Linkify(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return markdown;
        }

        var linkedCodePaths = InlineCodePathRegex().Replace(markdown, static match => ToMarkdownLink(match, includeSuffix: false));
        return LocalPathCandidateRegex().Replace(linkedCodePaths, match =>
            IsLinkablePathStart(linkedCodePaths, match.Index) ? ToMarkdownLink(match, includeSuffix: true) : match.Value);
    }

    private static bool IsLinkablePathStart(string markdown, int index)
    {
        if (index == 0)
        {
            return true;
        }

        var previous = markdown[index - 1];
        return previous is not (':' or '/' or '[' or '(' or '`') &&
               !char.IsLetterOrDigit(previous) && previous != '_';
    }

    private static string ToMarkdownLink(Match match, bool includeSuffix)
    {
        var path = FindExistingPath(match.Groups["path"].Value);
        if (path is null)
        {
            return match.Value;
        }

        var suffix = includeSuffix ? match.Value[path.ConsumedLength..] : string.Empty;
        var fileUri = new Uri(path.FullPath).AbsoluteUri;
        return $"[{path.FullPath}]({fileUri}){suffix}";
    }

    private static ExistingPath? FindExistingPath(string candidate)
    {
        var length = candidate.Length;
        while (length > 0)
        {
            var pathText = TrimTrailingTextPunctuation(candidate[..length]);
            if (pathText.Length == 0)
            {
                return null;
            }

            try
            {
                var fullPath = Path.GetFullPath(pathText);
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                {
                    return new ExistingPath(fullPath, pathText.Length);
                }
            }
            catch (ArgumentException)
            {
                // Not a valid local path; shrink the candidate and continue.
            }

            length = FindPreviousTextBoundary(pathText);
            if (length < 0)
            {
                return null;
            }
        }

        return null;
    }

    private static string TrimTrailingTextPunctuation(string value)
    {
        var length = value.Length;
        while (length > 0 && (char.IsPunctuation(value[length - 1]) || char.IsWhiteSpace(value[length - 1])))
        {
            length--;
        }

        return value[..length];
    }

    private static int FindPreviousTextBoundary(string value)
    {
        for (var index = value.Length - 1; index >= 0; index--)
        {
            if (char.IsWhiteSpace(value[index]) || IsTextPunctuation(value[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsTextPunctuation(char value) =>
        char.IsPunctuation(value) && value is not ('.' or '_' or '-' or '/' or '\\' or ':');

    private sealed record ExistingPath(string FullPath, int ConsumedLength);
}
