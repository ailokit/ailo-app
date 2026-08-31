using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ailo.AI;
using Ailo.Logging;
using LiveMarkdown.Avalonia;
using Markdig;
using Markdig.Extensions.AutoLinks;

namespace Ailo.Views;

/// <summary>
/// Binding-friendly adapter for LiveMarkdown's streaming renderer.
/// </summary>
public sealed class MarkdownView : UserControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));
    public static readonly StyledProperty<string?> ThinkingHeaderProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(ThinkingHeader));
    public static readonly StyledProperty<string?> ThinkingStatusProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(ThinkingStatus));

    private readonly ObservableStringBuilder _builder = new();
    private readonly MarkdownRenderer _renderer;
    private string _renderedMarkdown = string.Empty;

    static MarkdownView() => ConfigurePipeline();

    internal static void ConfigurePipeline()
    {
        if (Interlocked.Exchange(ref _pipelineConfigured, 1) != 0)
        {
            return;
        }

        MarkdownNode.Register<ThinkingBlockNode>();
        MarkdownRenderer.ConfigurePipeline += builder => builder
            .UseThinkingBlocks()
            .UseAutoLinks(new AutoLinkOptions
            {
                UseHttpsForWWWLinks = true
            });
    }

    private static int _pipelineConfigured;

    public MarkdownView()
    {
        _renderer = new MarkdownRenderer
        {
            MarkdownBuilder = _builder
        };
        _renderer.LinkClick += OnLinkClick;
        Content = _renderer;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, true);
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public string? ThinkingHeader
    {
        get => GetValue(ThinkingHeaderProperty);
        set => SetValue(ThinkingHeaderProperty, value);
    }

    public string? ThinkingStatus
    {
        get => GetValue(ThinkingStatusProperty);
        set => SetValue(ThinkingStatusProperty, value);
    }

    public async Task CopySelectionWithFormattingAsync()
    {
        var selectedBlocks = _renderer.GetVisualDescendants()
            .OfType<MarkdownTextBlock>()
            .Where(block => !string.IsNullOrEmpty(block.SelectedText))
            .ToList();
        if (selectedBlocks.Count == 0 || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        var plainText = string.Join(Environment.NewLine, selectedBlocks.Select(block => block.SelectedText));
        var html = string.Concat(selectedBlocks.Select(ToHtml));
        var htmlFormat = OperatingSystem.IsWindows()
            ? DataFormat.CreateStringPlatformFormat("HTML Format")
            : DataFormat.CreateStringPlatformFormat(OperatingSystem.IsMacOS() ? "public.html" : "text/html");

        var item = DataTransferItem.CreateText(plainText);
        item.Set(htmlFormat, OperatingSystem.IsWindows() ? ToWindowsClipboardHtml(html) : html);
        var data = new DataTransfer();
        data.Add(item);
        await clipboard.SetDataAsync(data);
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var commandModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key != Key.C || !e.KeyModifiers.HasFlag(commandModifier) || !_renderer.CanCopy)
        {
            return;
        }

        e.Handled = true;
        await CopySelectionWithFormattingAsync();
    }

    private static string ToHtml(MarkdownTextBlock block)
    {
        var text = WebUtility.HtmlEncode(block.SelectedText).Replace("\r\n", "<br>").Replace("\n", "<br>");
        var size = block.FontSize.ToString("0.##", CultureInfo.InvariantCulture);
        var weight = ToCssFontWeight(block.FontWeight);
        var style = block.FontStyle == Avalonia.Media.FontStyle.Italic ? "italic" : "normal";
        return $"<div style=\"font-family:{WebUtility.HtmlEncode(block.FontFamily.Name)};font-size:{size}px;font-weight:{weight};font-style:{style};white-space:pre-wrap\">{text}</div>";
    }

    private static int ToCssFontWeight(FontWeight weight) => weight switch
    {
        _ when weight == FontWeight.Thin => 100,
        _ when weight == FontWeight.ExtraLight => 200,
        _ when weight == FontWeight.Light => 300,
        _ when weight == FontWeight.Medium => 500,
        _ when weight == FontWeight.SemiBold => 600,
        _ when weight == FontWeight.Bold => 700,
        _ when weight == FontWeight.ExtraBold => 800,
        _ when weight == FontWeight.Black => 900,
        _ => 400
    };

    private static string ToWindowsClipboardHtml(string fragment)
    {
        const string headerTemplate = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string start = "<html><body><!--StartFragment-->";
        const string end = "<!--EndFragment--></body></html>";
        var emptyHeader = string.Format(CultureInfo.InvariantCulture, headerTemplate, 0, 0, 0, 0);
        var startHtml = Encoding.UTF8.GetByteCount(emptyHeader);
        var startFragment = startHtml + Encoding.UTF8.GetByteCount(start);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(end);
        return string.Format(CultureInfo.InvariantCulture, headerTemplate, startHtml, endHtml, startFragment, endFragment) + start + fragment + end;
    }

    private static void OnLinkClick(object? sender, LinkClickedEventArgs e)
    {
        if (e.HRef is not { } uri)
        {
            return;
        }

        try
        {
            if (uri.Scheme is "http" or "https")
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            else if (uri.IsFile && (File.Exists(uri.LocalPath) || Directory.Exists(uri.LocalPath)))
            {
                Process.Start(new ProcessStartInfo(uri.LocalPath) { UseShellExecute = true });
            }
            else
            {
                return;
            }

            e.Handled = true;
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(MarkdownView), "Failed to open a markdown link", Microsoft.Extensions.Logging.LogLevel.Warning);
            // Opening a link is best-effort; an unavailable system browser must not crash the chat window.
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != MarkdownProperty)
        {
            if (change.Property == ThinkingHeaderProperty)
            {
                ThinkingBlockControl.SetHeader(this, ThinkingHeader);
                UpdateThinkingHeaders();
            }
            else if (change.Property == ThinkingStatusProperty)
            {
                UpdateThinkingActivity();
            }

            return;
        }

        var previousExpansionStates = CaptureThinkingExpansionStates();
        var markdown = change.NewValue is string value
            ? LocalPathMarkdownLinkifier.Linkify(ThinkingMarkdown.NormalizeForRendering(value))
            : string.Empty;
        if (markdown.StartsWith(_renderedMarkdown, StringComparison.Ordinal))
        {
            _builder.Append(markdown[_renderedMarkdown.Length..]);
        }
        else
        {
            _builder.Clear();
            _builder.Append(markdown);
        }

        _renderedMarkdown = markdown;
        ThinkingBlockControl.SetHeader(this, ThinkingHeader);
        UpdateThinkingHeaders();
        RestoreThinkingExpansionStates(previousExpansionStates);
        UpdateThinkingActivity();
    }

    private void UpdateThinkingHeaders()
    {
        if (string.IsNullOrWhiteSpace(ThinkingHeader))
        {
            return;
        }

        foreach (var expander in _renderer.GetVisualDescendants().OfType<ThinkingBlockControl>())
        {
            expander.SetDefaultHeader(ThinkingHeader);
        }
    }

    private IReadOnlyList<(bool Expanded, bool UserChanged)> CaptureThinkingExpansionStates() =>
        _renderer.GetVisualDescendants()
            .OfType<ThinkingBlockControl>()
            .Select(expander => (expander.IsExpanded, expander.UserChangedExpansion))
            .ToArray();

    private void RestoreThinkingExpansionStates(IReadOnlyList<(bool Expanded, bool UserChanged)> states)
    {
        var expanders = _renderer.GetVisualDescendants().OfType<ThinkingBlockControl>().ToList();
        for (var index = 0; index < expanders.Count; index++)
        {
            expanders[index].RestoreUserExpansion(
                index < states.Count && states[index].Expanded,
                index < states.Count && states[index].UserChanged);
        }
    }

    private void UpdateThinkingActivity()
    {
        var expanders = _renderer.GetVisualDescendants().OfType<ThinkingBlockControl>().ToList();
        for (var index = 0; index < expanders.Count; index++)
        {
            var active = index == expanders.Count - 1 && !string.IsNullOrWhiteSpace(ThinkingStatus);
            expanders[index].SetActive(active, ThinkingStatus);
        }
    }
}
