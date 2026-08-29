using System.Windows.Input;
using Ailo.AI.Providers;

namespace Ailo.ViewModels;

public sealed class ProviderModelMenuItem(ApiProvider provider, IReadOnlyList<ModelMenuItem> models)
{
    public string Name => provider.Name;
    public IReadOnlyList<ModelMenuItem> Models { get; } = models;
}

public sealed class ModelMenuItem(string modelId, ICommand selectCommand)
{
    public string ModelId { get; } = modelId;
    public ICommand SelectCommand { get; } = selectCommand;
}
