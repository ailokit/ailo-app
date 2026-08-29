using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Data.Converters;
using Ailo.AI.Providers;
using CommunityToolkit.Mvvm.Input;

namespace Ailo.ViewModels;

public class AiProviderMenuDataConverter : IMultiValueConverter
{
    public static AiProviderMenuDataConverter Instance { get; } = new AiProviderMenuDataConverter();

    private ObservableCollection<ModelMenuItem> ConvertProviderMenu(
        ChatWindowViewModel vm,
        ApiProvider provider)
    {
        var menuData = provider.ModelIds
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => new ModelMenuItem(
                model, 
                new RelayCommand(() => vm.SelectModel(provider, model))))
            .ToArray();
        return [.. menuData];
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 1)
        {
            return null;
        }

        var value = values[0];
        var vmObj = values[1];
        if (value is null || vmObj is null)
        {
            return value;
        }

        if (vmObj is ChatWindowViewModel vm && value is ApiProvider provider)
        {
            return ConvertProviderMenu(vm, provider);
        }

        return null;
    }
}