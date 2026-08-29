using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ailo.AI.Skills;
using Ailo.Localization;
using Ailo.Logging;
using Ailo.Services;

namespace Ailo.ViewModels;

public sealed partial class SkillSettingsViewModel : SettingsViewModelBase
{
    private readonly SkillRepository _skills;
    private readonly SkillService _skillService;
    private readonly AppState _appState;
    private readonly IConfirmationService? _confirmation;

    public SkillSettingsViewModel(
        SkillRepository skills,
        SkillService skillService,
        LocalizationService localization,
        AppState appState,
        IConfirmationService? confirmation = null)
        : base(localization)
    {
        _skills = skills;
        _skillService = skillService;
        _appState = appState;
        _confirmation = confirmation;
    }

    public ObservableCollection<Skill> Skills { get; } = [];

    [ObservableProperty] private Skill? _selectedSkill;

    [ObservableProperty] private string _skillName = string.Empty;

    [ObservableProperty] private string _skillPrompt = string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Skills.Clear();
        foreach (var skill in await _skills.GetEnabledAsync(cancellationToken).ConfigureAwait(false))
            Skills.Add(skill);

        if (SelectedSkill is null && Skills.Count > 0)
            SelectedSkill = Skills[0];
    }

    [RelayCommand]
    private void AddSkill()
    {
        SelectedSkill = null;
        SkillName = string.Empty;
        SkillPrompt = string.Empty;
        StatusMessage = T("AddingSkill");
    }

    [RelayCommand]
    private async Task SaveSkillAsync()
    {
        var existing = SelectedSkill;
        var now = DateTimeOffset.UtcNow;
        try
        {
            var saved = await _skillService.SaveCustomAsync(new Skill(
                existing?.Id ?? Guid.NewGuid().ToString("N"),
                SkillName, null, SkillPrompt, null, false, true,
                existing?.SortOrder ?? Skills.Count + 100,
                existing?.Version ?? 0,
                existing?.CreatedAt ?? now, now)).ConfigureAwait(false);

            await ReloadSkillsAsync().ConfigureAwait(false);
            SelectedSkill = Skills.FirstOrDefault(item => item.Id == saved.Id);
            StatusMessage = T("SkillSaved");
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(SkillSettingsViewModel), "Failed to save skill");
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteSkillAsync(Skill? skill)
    {
        if (skill is null) return;

        if (_confirmation is not null && !await _confirmation.ConfirmDeleteAsync(skill.Name))
            return;

        try
        {
            await _skillService.DeleteCustomAsync(skill.Id).ConfigureAwait(false);
            await ReloadSkillsAsync().ConfigureAwait(false);
            SelectedSkill = Skills.FirstOrDefault();
            StatusMessage = T("SkillDeleted");
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(SkillSettingsViewModel), "Failed to delete skill");
            StatusMessage = exception.Message;
        }
    }

    public async Task ReorderSkillsAsync(Skill draggedSkill, Skill? targetSkill, bool insertAfter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draggedSkill);
        var draggedIndex = Skills.IndexOf(draggedSkill);
        if (draggedIndex < 0) return;

        var reordered = Skills.ToList();
        reordered.RemoveAt(draggedIndex);

        var targetIndex = targetSkill is null ? reordered.Count : reordered.IndexOf(targetSkill);
        if (targetIndex < 0) return;
        if (targetSkill is not null && insertAfter) targetIndex++;

        reordered.Insert(Math.Clamp(targetIndex, 0, reordered.Count), draggedSkill);
        if (reordered.SequenceEqual(Skills)) return;

        await _skills.ReorderEnabledAsync(reordered.Select(s => s.Id).ToArray(), cancellationToken);
        Skills.Clear();
        foreach (var skill in reordered)
            Skills.Add(skill with { SortOrder = Skills.Count });

        StatusMessage = T("SkillSaved");
    }

    private async Task ReloadSkillsAsync(CancellationToken cancellationToken = default)
    {
        Skills.Clear();
        foreach (var skill in await _skills.GetEnabledAsync(cancellationToken).ConfigureAwait(false))
            Skills.Add(skill);
        
        // Keep the chat window's live skill selection in sync before reporting
        // that the prompt was saved. Otherwise a newly selected prompt can race
        // the background reload and capture the previous SystemPrompt.
        await _appState.ReloadAiSkillsAsync(cancellationToken).ConfigureAwait(false);
    }

    partial void OnSelectedSkillChanged(Skill? value)
    {
        if (value is not null)
        {
            SkillName = value.Name;
            SkillPrompt = value.SystemPrompt;
        }
    }
}
