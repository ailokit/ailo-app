using CommunityToolkit.Mvvm.ComponentModel;

namespace Ailo.ViewModels;

public sealed partial class ProviderModelItem : ViewModelBase
{
    public ProviderModelItem(string modelId = "", bool isMultimodal = false)
    {
        ModelId = modelId;
        IsMultimodal = isMultimodal;
    }

    [ObservableProperty] private string _modelId;
    [ObservableProperty] private bool _isMultimodal;
}
