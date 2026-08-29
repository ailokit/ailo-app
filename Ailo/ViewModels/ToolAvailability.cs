using CommunityToolkit.Mvvm.ComponentModel;
using Ailo.Localization;

namespace Ailo.ViewModels;

/// <summary>Global availability of one tool. The same instances are shared by all windows.</summary>
public sealed partial class ToolAvailability : ObservableObject
{
    private readonly string? _displayNameKey;
    private readonly string? _descriptionKey;

    public ToolAvailability(string name, string displayName, string description, bool isEnabled = true)
    {
        Name = name;
        CategoryKey = "ToolCategoryOther";
        DisplayName = displayName;
        Description = description;
        IsEnabled = isEnabled;
    }

    public ToolAvailability(string name, string displayNameKey, string descriptionKey,
        LocalizationService localization, bool isEnabled = true)
        : this(name, displayNameKey, descriptionKey, "ToolCategoryOther", localization, isEnabled)
    {
    }

    public ToolAvailability(string name, string displayNameKey, string descriptionKey, string categoryKey,
        LocalizationService localization, bool isEnabled = true)
    {
        Name = name;
        CategoryKey = categoryKey;
        _displayNameKey = displayNameKey;
        _descriptionKey = descriptionKey;
        DisplayName = localization[displayNameKey];
        Description = localization[descriptionKey];
        IsEnabled = isEnabled;
    }

    public string Name { get; }
    public string CategoryKey { get; }

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    public void RefreshLocalization(LocalizationService localization)
    {
        if (_displayNameKey is null || _descriptionKey is null) return;
        DisplayName = localization[_displayNameKey];
        Description = localization[_descriptionKey];
    }

    [ObservableProperty]
    private bool _isEnabled;
}

/// <summary>Per-chat selection of a globally enabled tool.</summary>
public sealed partial class SessionToolSelection : ObservableObject
{
    public SessionToolSelection(string name, string displayName, string description, bool isSelected = true)
    {
        Name = name;
        DisplayName = displayName;
        Description = description;
        IsSelected = isSelected;
    }

    public string Name { get; }
    public string DisplayName { get; }
    public string Description { get; }

    [ObservableProperty]
    private bool _isSelected;
}
