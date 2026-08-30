using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ailo.ViewModels;

public sealed partial class AccentColorOption(string key, string displayName, string hex) : ObservableObject
{
    public string Key { get; } = key;
    public string DisplayName { get; } = displayName;
    public string Hex { get; } = hex;
    public Color Color => Color.Parse(Hex);
    public IBrush Brush => new SolidColorBrush(Color);

    [ObservableProperty]
    private bool _isSelected;
}
