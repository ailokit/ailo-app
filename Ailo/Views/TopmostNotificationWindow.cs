using Avalonia.Interactivity;

namespace Ailo.Views;

/// <summary>A user-dismissible notification surface that remains above other application windows.</summary>
internal sealed partial class TopmostNotificationWindow : Avalonia.Controls.Window
{
    public TopmostNotificationWindow(string title, string body, string? subtitle)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        BodyMarkdown.Markdown = body;
        SubtitleText.Text = subtitle;
        SubtitleText.IsVisible = !string.IsNullOrWhiteSpace(subtitle);
        CloseButton.Content = Ailo.Localization.Resources.ResourceManager.GetString("Close", Ailo.Localization.Resources.Culture) ?? "Close";
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
