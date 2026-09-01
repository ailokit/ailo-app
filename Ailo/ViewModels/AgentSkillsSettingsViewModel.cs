using System.Collections.ObjectModel;
using Ailo.AI.Skills;
using Ailo.Localization;
using Ailo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ailo.ViewModels;

/// <summary>Settings-page model for file-based Agent Skills discovered on this computer.</summary>
public sealed partial class AgentSkillsSettingsViewModel : SettingsViewModelBase
{
    private readonly AgentSkillsService _agentSkills;
    private readonly IConfirmationService? _confirmation;

    public AgentSkillsSettingsViewModel(
        AgentSkillsService agentSkills,
        LocalizationService localization,
        IConfirmationService? confirmation = null)
        : base(localization)
    {
        _agentSkills = agentSkills;
        _confirmation = confirmation;
    }

    public ObservableCollection<AgentSkillSourceGroupViewModel> Groups { get; } = [];

    [ObservableProperty] private AgentSkillSourceGroupViewModel? _selectedGroup;
    [ObservableProperty] private AgentSkillItemViewModel? _selectedSkill;

    public bool HasSelectedSkill => SelectedSkill is not null;

    public Task LoadAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    [RelayCommand]
    private Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    [RelayCommand(CanExecute = nameof(CanUninstallSkill))]
    private async Task UninstallSkillAsync()
    {
        var skill = SelectedSkill;
        if (skill is null)
            return;

        var confirmed = _confirmation is null ||
            (skill.IsAiloSource
                ? await _confirmation.ConfirmDeleteAsync(skill.Name)
                : await _confirmation.ConfirmDeleteWithWarningAsync(
                    skill.Name,
                    string.Format(T("AgentSkillsExternalUninstallWarning"), skill.SourcePath)));
        if (!confirmed)
            return;

        try
        {
            await _agentSkills.UninstallAsync(skill.DirectoryPath);
            await RefreshAsync(CancellationToken.None);
            StatusMessage = T("AgentSkillsUninstalled");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private bool CanUninstallSkill() => SelectedSkill is not null;

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            // This view is initialized from the settings window. Keep the continuation on
            // Avalonia's UI context because Groups is bound directly by the page.
            var selectedDirectory = SelectedSkill?.DirectoryPath;
            var selectedSource = SelectedGroup?.Source;
            var skills = await _agentSkills.RefreshAsync(cancellationToken);
            var grouped = skills
                .GroupBy(skill => new { skill.Source, skill.SourceRoot })
                .OrderBy(group => group.Key.Source)
                .Select(group => new AgentSkillSourceGroupViewModel(
                    group.Key.Source,
                    group.Key.SourceRoot,
                    group.OrderBy(skill => skill.Name).Select(skill => new AgentSkillItemViewModel(skill, T("AgentSkillsEnabled"), T("AgentSkillsScripts"), SetEnabledAsync))))
                .ToArray();

            Groups.Clear();
            foreach (var group in grouped)
                Groups.Add(group);
            SelectedGroup = Groups.FirstOrDefault(group => group.Source == selectedSource) ?? Groups.FirstOrDefault();
            SelectedSkill = SelectedGroup?.Skills.FirstOrDefault(skill => skill.DirectoryPath == selectedDirectory)
                ?? SelectedGroup?.Skills.FirstOrDefault();
            StatusMessage = grouped.Length == 0 ? T("AgentSkillsEmpty") : string.Empty;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task SetEnabledAsync(string directoryPath, bool enabled)
    {
        try
        {
            await _agentSkills.SetEnabledAsync(directoryPath, enabled);
            StatusMessage = T("AgentSkillsSaved");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            throw;
        }
    }

    partial void OnSelectedGroupChanged(AgentSkillSourceGroupViewModel? value)
    {
        if (value is null)
        {
            SelectedSkill = null;
            return;
        }

        if (SelectedSkill is null || !value.Skills.Contains(SelectedSkill))
            SelectedSkill = value.Skills.FirstOrDefault();
    }

    partial void OnSelectedSkillChanged(AgentSkillItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedSkill));
        UninstallSkillCommand.NotifyCanExecuteChanged();
    }
}

public sealed class AgentSkillSourceGroupViewModel(
    string source,
    string sourcePath,
    IEnumerable<AgentSkillItemViewModel> skills)
{
    public string Source { get; } = source;
    public string SourcePath { get; } = sourcePath;
    public ObservableCollection<AgentSkillItemViewModel> Skills { get; } = new(skills);
}

public sealed partial class AgentSkillItemViewModel : ObservableObject
{
    private readonly Func<string, bool, Task> _setEnabled;
    private bool _ready;

    public AgentSkillItemViewModel(AgentSkillDefinition skill, string enabledText, string scriptsText, Func<string, bool, Task> setEnabled)
    {
        Id = skill.Id;
        Source = skill.Source;
        SourcePath = skill.SourceRoot;
        Name = skill.Name;
        Description = skill.Description;
        DirectoryPath = skill.DirectoryPath;
        HasScripts = skill.HasScripts;
        IsAiloSource = string.Equals(skill.Source, "Ailo", StringComparison.OrdinalIgnoreCase);
        EnabledText = enabledText;
        ScriptsText = scriptsText;
        _setEnabled = setEnabled;
        _isEnabled = skill.IsEnabled;
        _ready = true;
    }

    public string Id { get; }
    public string Source { get; }
    public string SourcePath { get; }
    public string Name { get; }
    public string Description { get; }
    public string DirectoryPath { get; }
    public string SkillFilePath => Path.Combine(DirectoryPath, "SKILL.md");
    public bool HasScripts { get; }
    public bool IsAiloSource { get; }
    public string EnabledText { get; }
    public string ScriptsText { get; }

    [ObservableProperty] private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_ready)
            _ = PersistAsync(value);
    }

    private async Task PersistAsync(bool enabled)
    {
        try
        {
            await _setEnabled(DirectoryPath, enabled);
        }
        catch
        {
            _ready = false;
            IsEnabled = !enabled;
            _ready = true;
        }
    }
}
