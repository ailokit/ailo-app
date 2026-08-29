using Ailo.AI.Tools;
using Ailo.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ailo.ViewModels;

/// <summary>Shell backend shown in the Tools settings page dropdown.</summary>
public sealed class ShellToolOption : ObservableObject, IDisposable
{
    private readonly ShellToolConfiguration _configuration;
    private readonly string _unavailableDescription;

    public ShellToolOption(
        ShellToolConfiguration configuration,
        LocalizationService localization,
        ShellToolKind kind,
        string displayNameKey,
        string descriptionKey)
    {
        _configuration = configuration;
        Kind = kind;
        DisplayName = localization[displayNameKey];
        Description = localization[descriptionKey];
        _unavailableDescription = localization["ShellToolDockerUnavailable"];
        _configuration.PropertyChanged += OnConfigurationChanged;
    }

    public ShellToolKind Kind { get; }
    public string DisplayName { get; }
    public string Description { get; }

    public bool IsAvailable => Kind != ShellToolKind.Docker || _configuration.IsDockerShellAvailable;

    public string AvailabilityDescription => IsAvailable ? string.Empty : _unavailableDescription;

    private void OnConfigurationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellToolConfiguration.IsDockerShellAvailable))
        {
            OnPropertyChanged(nameof(IsAvailable));
            OnPropertyChanged(nameof(AvailabilityDescription));
        }
    }

    public void Dispose() => _configuration.PropertyChanged -= OnConfigurationChanged;
}
