using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LiveMarkdown.Avalonia;
using Material.Icons;
using Material.Icons.Avalonia;

namespace Ailo.Views;

/// <summary>Renders each ordered <c>thinking</c> fence as an independently collapsible block.</summary>
public sealed class ThinkingBlockNode : BlockNode<ThinkingCodeBlock>
{
    private readonly ObservableStringBuilder _builder = new();
    private readonly MarkdownRenderer _renderer;
    private readonly ThinkingBlockControl _control;

    public override Control Control => _control;

    public ThinkingBlockNode()
    {
        _renderer = new MarkdownRenderer
        {
            MarkdownBuilder = _builder
        };

        _control = new ThinkingBlockControl(_renderer)
        {
            IsExpanded = false,
            Padding = new Avalonia.Thickness(0),
            Margin = new Avalonia.Thickness(0, 0, 0, 2),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
    }

    protected override bool UpdateCore(
        DocumentNode documentNode,
        ThinkingCodeBlock block,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        if (block.Lines.Lines is null)
        {
            return false;
        }

        var text = string.Join(
            Environment.NewLine,
            block.Lines.Lines.Take(block.Lines.Count).Select(line => line.Slice.ToString()));

        _builder.Clear();
        _builder.Append(text);
        return true;
    }
}

/// <summary>
/// A compact, header-clickable container for model reasoning and tool notices.
/// </summary>
public sealed class ThinkingBlockControl : Border
{
    /// <summary>
    /// Localized header inherited from the containing <see cref="MarkdownView"/>.
    /// Inheritance is important for historical messages because their nodes can be
    /// materialized after the MarkdownView's initial content update.
    /// </summary>
    public static readonly AttachedProperty<string?> HeaderProperty =
        AvaloniaProperty.RegisterAttached<ThinkingBlockControl, Control, string?>("Header", inherits: true);

    private readonly Border _header;
    private readonly Border _content;
    private readonly MaterialIcon _indicator = new()
    {
        Kind = MaterialIconKind.ChevronRight,
        Width = 12,
        Height = 12,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
    };
    private readonly TextBlock _headerText = new()
    {
        FontSize = 12,
        FontWeight = FontWeight.Medium,
        Margin = new Avalonia.Thickness(0)
    };
    private string _defaultHeader = "Thinking";
    private bool _isExpanded;
    private bool _userChangedExpansion;

    public ThinkingBlockControl(MarkdownRenderer renderer)
    {
        _headerText.Text = _defaultHeader;

        _header = new Border
        {
            Classes = { "thinking-header" },
            Padding = new Avalonia.Thickness(2, 1),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4,
                Margin = new Avalonia.Thickness(0),
                Children = { _indicator, _headerText }
            }
        };

        _content = new Border
        {
            Classes = { "thinking-content" },
            IsVisible = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Child = renderer
        };

        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        renderer.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        Child = new StackPanel
        {
            Spacing = 0,
            Children = { _header, _content }
        };
        _header.PointerPressed += OnHeaderPointerPressed;
    }

    public static void SetHeader(Control element, string? value) => element.SetValue(HeaderProperty, value);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HeaderProperty)
        {
            SetDefaultHeader(change.NewValue as string);
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            _content.IsVisible = value;
            _indicator.Kind = value ? MaterialIconKind.ChevronDown : MaterialIconKind.ChevronRight;
        }
    }

    internal bool UserChangedExpansion => _userChangedExpansion;

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_header).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _userChangedExpansion = true;
        IsExpanded = !IsExpanded;
        e.Handled = true;
    }

    /// <summary>Restores only a state that was previously chosen by the user.</summary>
    internal void RestoreUserExpansion(bool expanded, bool userChangedExpansion)
    {
        _userChangedExpansion = userChangedExpansion;
        IsExpanded = _userChangedExpansion && expanded;
    }

    public void SetDefaultHeader(string? header)
    {
        if (!string.IsNullOrWhiteSpace(header))
        {
            _defaultHeader = header;
        }

        if (!Classes.Contains("thinking-active"))
        {
            _headerText.Text = _defaultHeader;
        }
    }

    public void SetActive(bool active, string? status)
    {
        if (active && !string.IsNullOrWhiteSpace(status))
        {
            Classes.Add("thinking-active");
            _headerText.Text = status.EndsWith('…') ? status : status + "…";
            return;
        }

        Classes.Remove("thinking-active");
        _headerText.Text = _defaultHeader;
    }
}
