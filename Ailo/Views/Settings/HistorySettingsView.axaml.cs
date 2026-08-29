using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Ailo.ViewModels;

namespace Ailo.Views.Settings;

public partial class HistorySettingsView : UserControl
{
    public HistorySettingsView() => InitializeComponent();

    private void OnHistoryScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not HistorySettingsViewModel viewModel || sender is not ScrollViewer scrollViewer) return;
        if (scrollViewer.Offset.Y + scrollViewer.Viewport.Height >= scrollViewer.Extent.Height - 80)
            viewModel.LoadMoreHistoryCommand.Execute(null);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is HistorySettingsViewModel viewModel)
            viewModel.LoadHistoryCommand.Execute(null);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is HistorySettingsViewModel viewModel)
            viewModel.LoadHistoryCommand.Execute(null);
    }
}
