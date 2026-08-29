using Avalonia.Controls;
using Avalonia.Interactivity;
using Ailo.ViewModels;

namespace Ailo.Views.Settings;

public partial class ApiKeySettingsView : UserControl
{
    public ApiKeySettingsView() => InitializeComponent();

    private void OnFetchedModelClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string modelId } && DataContext is ApiKeySettingsViewModel vm)
        {
            vm.AddFetchedModelCommand.Execute(modelId);
        }
    }
}
