using Avalonia.Interactivity;

namespace Ailo.Views;

public partial class ConfirmationDialog : Avalonia.Controls.Window
{
    public ConfirmationDialog() => InitializeComponent();

    public ConfirmationDialog(string title, string message, string cancelLabel, string deleteLabel, string? warningMessage = null)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        WarningText.Text = warningMessage;
        WarningText.IsVisible = !string.IsNullOrWhiteSpace(warningMessage);
        CancelButton.Content = cancelLabel;
        DeleteButton.Content = deleteLabel;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnDeleteClick(object? sender, RoutedEventArgs e) => Close(true);
}
