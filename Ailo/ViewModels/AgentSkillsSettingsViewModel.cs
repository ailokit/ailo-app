using System.Collections.ObjectModel;
using Ailo.AI.Skills;
using Ailo.Localization;
using Ailo.Services;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ailo.ViewModels;

/// <summary>Settings-page model for file-based Agent Skills discovered on this computer.</summary>
public sealed partial class AgentSkillsSettingsViewModel : SettingsViewModelBase
{
    private readonly AgentSkillsService _agentSkills;
    private readonly IConfirmationService? _confirmation;
    private readonly ISystemBrowserService? _browser;
    private IStorageProvider? _storageProvider;
    private IClipboard? _clipboard;
    private AgentSkillRepositoryScan? _repositoryScan;
    private CancellationTokenSource? _scanCancellation;

    public AgentSkillsSettingsViewModel(
        AgentSkillsService agentSkills,
        LocalizationService localization,
        IConfirmationService? confirmation = null,
        ISystemBrowserService? browser = null)
        : base(localization)
    {
        _agentSkills = agentSkills;
        _confirmation = confirmation;
        _browser = browser;
    }

    public ObservableCollection<AgentSkillSourceGroupViewModel> Groups { get; } = [];
    public ObservableCollection<AgentSkillInstallCandidateViewModel> InstallCandidates { get; } = [];
    public ObservableCollection<AgentSkillInstallCandidateViewModel> VisibleInstallCandidates { get; } = [];
    public IReadOnlyList<AgentSkillInstallType> InstallTypes => _agentSkills.InstallTypes;

    [ObservableProperty] private AgentSkillSourceGroupViewModel? _selectedGroup;
    [ObservableProperty] private AgentSkillItemViewModel? _selectedSkill;
    [ObservableProperty] private string _repositoryUrl = string.Empty;
    [ObservableProperty] private AgentSkillInstallType? _selectedInstallType;
    [ObservableProperty] private string _customInstallDirectory = string.Empty;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private bool _isUpdatingSkill;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _selectAllInstallCandidates;
    [ObservableProperty] private string _installSearchText = string.Empty;
    [ObservableProperty] private string _scanStatusMessage = string.Empty;
    [ObservableProperty] private bool _hasScanError;

    private bool _synchronizingInstallSelection;

    public bool HasSelectedSkill => SelectedSkill is not null;
    public bool HasInstallCandidates => InstallCandidates.Count > 0;
    public bool HasSelectedInstallCandidates => InstallCandidates.Any(candidate => candidate.IsSelected);
    public string InstallTargetDirectory
    {
        get
        {
            if (SelectedInstallType is null)
                return string.Empty;

            try
            {
                return SelectedInstallType.GetInstallDirectory(CustomInstallDirectory);
            }
            catch (ArgumentException)
            {
                return CustomInstallDirectory;
            }
        }
    }
    public bool CanScanRepository => !IsScanning && !string.IsNullOrWhiteSpace(RepositoryUrl);

    public Task LoadAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    [RelayCommand]
    private Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    [RelayCommand]
    private void BeginInstall()
    {
        DisposeRepositoryScan();
        _scanCancellation?.Cancel();
        RepositoryUrl = string.Empty;
        CustomInstallDirectory = _agentSkills.CustomSkillsDirectory ?? string.Empty;
        InstallSearchText = string.Empty;
        SelectedInstallType = InstallTypes.FirstOrDefault();
        InstallCandidates.Clear();
        VisibleInstallCandidates.Clear();
        SelectAllInstallCandidates = false;
        ScanStatusMessage = string.Empty;
        HasScanError = false;
        IsInstalling = true;
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(HasInstallCandidates));
        OnPropertyChanged(nameof(HasSelectedInstallCandidates));
    }

    [RelayCommand]
    private void CancelInstall()
    {
        DisposeRepositoryScan();
        _scanCancellation?.Cancel();
        InstallCandidates.Clear();
        VisibleInstallCandidates.Clear();
        InstallSearchText = string.Empty;
        SelectAllInstallCandidates = false;
        ScanStatusMessage = string.Empty;
        HasScanError = false;
        IsInstalling = false;
        OnPropertyChanged(nameof(HasInstallCandidates));
        OnPropertyChanged(nameof(HasSelectedInstallCandidates));
        StatusMessage = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanScanRepository))]
    private async Task ScanRepositoryAsync()
    {
        _scanCancellation?.Cancel();
        using var scanCancellation = new CancellationTokenSource();
        _scanCancellation = scanCancellation;
        IsScanning = true;
        HasScanError = false;
        ScanStatusMessage = T("AgentSkillsCloningRepository");
        StatusMessage = ScanStatusMessage;
        DisposeRepositoryScan();
        InstallCandidates.Clear();
        VisibleInstallCandidates.Clear();
        InstallSearchText = string.Empty;
        SelectAllInstallCandidates = false;
        OnPropertyChanged(nameof(HasInstallCandidates));
        OnPropertyChanged(nameof(HasSelectedInstallCandidates));
        var progress = new Progress<AgentSkillScanStep>(step =>
        {
            ScanStatusMessage = step switch
            {
                AgentSkillScanStep.CloningRepository => T("AgentSkillsCloningRepository"),
                AgentSkillScanStep.ScanningSkills => T("AgentSkillsScanningSkills"),
                _ => T("AgentSkillsScanning")
            };
            StatusMessage = ScanStatusMessage;
        });
        try
        {
            _repositoryScan = await _agentSkills.ScanRepositoryAsync(
                RepositoryUrl,
                scanCancellation.Token,
                progress);
            scanCancellation.Token.ThrowIfCancellationRequested();
            foreach (var candidate in _repositoryScan.Skills)
                InstallCandidates.Add(new AgentSkillInstallCandidateViewModel(candidate, OnInstallCandidateSelectionChanged));
            RefreshVisibleInstallCandidates();
            SelectAllInstallCandidates = InstallCandidates.Count > 0;
            OnPropertyChanged(nameof(HasInstallCandidates));
            OnPropertyChanged(nameof(HasSelectedInstallCandidates));
            StatusMessage = InstallCandidates.Count == 0 ? T("AgentSkillsInstallEmpty") : T("AgentSkillsScanCompleted");
        }
        catch (OperationCanceledException) when (scanCancellation.IsCancellationRequested)
        {
            ScanStatusMessage = string.Empty;
            HasScanError = false;
        }
        catch (Exception exception)
        {
            ScanStatusMessage = string.Format(T("AgentSkillsScanFailed"), exception.Message);
            StatusMessage = ScanStatusMessage;
            HasScanError = true;
        }
        finally
        {
            if (ReferenceEquals(_scanCancellation, scanCancellation))
                _scanCancellation = null;
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task InstallSelectedSkillsAsync()
    {
        if (_repositoryScan is null || SelectedInstallType is null)
        {
            StatusMessage = T("AgentSkillsInstallRequired");
            return;
        }

        var selectedIds = InstallCandidates
            .Where(candidate => candidate.IsSelected)
            .Select(candidate => candidate.Id)
            .ToArray();
        if (selectedIds.Length == 0)
        {
            StatusMessage = T("AgentSkillsInstallSelectSkills");
            return;
        }

        try
        {
            var installed = await _agentSkills.InstallAsync(
                _repositoryScan,
                selectedIds,
                SelectedInstallType,
                CustomInstallDirectory);
        DisposeRepositoryScan();
        _scanCancellation?.Cancel();
        InstallCandidates.Clear();
            VisibleInstallCandidates.Clear();
        InstallSearchText = string.Empty;
        ScanStatusMessage = string.Empty;
        HasScanError = false;
            SelectAllInstallCandidates = false;
            IsInstalling = false;
            OnPropertyChanged(nameof(HasInstallCandidates));
            OnPropertyChanged(nameof(HasSelectedInstallCandidates));
            await RefreshAsync(CancellationToken.None);
            StatusMessage = string.Format(T("AgentSkillsInstalled"), installed.Count, InstallTargetDirectory);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task BrowseInstallDirectoryAsync()
    {
        if (_storageProvider is null)
            return;

        var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = T("AgentSkillsSelectInstallDirectory"),
            AllowMultiple = false
        });
        var selectedPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(selectedPath))
            CustomInstallDirectory = selectedPath;
    }

    public void AttachStorageProvider(IStorageProvider? storageProvider) => _storageProvider = storageProvider;

    public void AttachClipboard(IClipboard? clipboard) => _clipboard = clipboard;

    [RelayCommand(CanExecute = nameof(CanUpdateSkill))]
    private async Task UpdateSkillAsync()
    {
        var skill = SelectedSkill;
        if (skill is null || !skill.HasInstallMetadata)
            return;

        IsUpdatingSkill = true;
        StatusMessage = T("AgentSkillsUpdating");
        var progress = new Progress<AgentSkillScanStep>(step =>
        {
            StatusMessage = step switch
            {
                AgentSkillScanStep.CloningRepository => T("AgentSkillsCloningRepository"),
                AgentSkillScanStep.ScanningSkills => T("AgentSkillsScanningSkills"),
                _ => T("AgentSkillsUpdating")
            };
        });
        try
        {
            await _agentSkills.UpdateAsync(skill.Definition, progress: progress);
            await RefreshAsync(CancellationToken.None);
            StatusMessage = T("AgentSkillsUpdated");
        }
        catch (AgentSkillUpdateUnavailableException)
        {
            var confirmed = _confirmation is null || await _confirmation.ConfirmDeleteWithWarningAsync(
                skill.Name,
                T("AgentSkillsUpdateMissingWarning"));
            if (!confirmed)
            {
                StatusMessage = T("AgentSkillsUpdateCancelled");
                return;
            }

            await _agentSkills.UninstallAsync(skill.DirectoryPath);
            await RefreshAsync(CancellationToken.None);
            StatusMessage = T("AgentSkillsRemovedAfterUpdateMissing");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsUpdatingSkill = false;
        }
    }

    private bool CanUpdateSkill() => !IsUpdatingSkill && SelectedSkill?.HasInstallMetadata == true;

    [RelayCommand]
    private async Task CopySkillPathAsync()
    {
        if (SelectedSkill is null || _clipboard is null)
            return;

        try
        {
            await _clipboard.SetTextAsync(SelectedSkill.SkillFilePath);
            StatusMessage = T("AgentSkillsPathCopied");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private void OpenSkillFolder()
    {
        if (SelectedSkill is null || _browser is null)
            return;

        try
        {
            _browser.Open(new UriBuilder(Uri.UriSchemeFile, string.Empty)
            {
                Path = SelectedSkill.DirectoryPath
            }.Uri);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

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
                .OrderBy(group => string.Equals(group.Key.Source, "Ailo", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(group => group.Key.Source, StringComparer.OrdinalIgnoreCase)
                .Select(group => new AgentSkillSourceGroupViewModel(
                    group.Key.Source,
                    group.Key.SourceRoot,
                    group.OrderBy(skill => skill.Name).Select(skill => new AgentSkillItemViewModel(skill, T("AgentSkillsEnabled"), T("AgentSkillsScripts"), SetEnabledAsync)),
                    string.Equals(group.Key.Source, "Custom", StringComparison.OrdinalIgnoreCase) ? T("AgentSkillsCustom") : null))
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
        UpdateSkillCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsUpdatingSkillChanged(bool value) => UpdateSkillCommand.NotifyCanExecuteChanged();

    partial void OnRepositoryUrlChanged(string value)
    {
        OnPropertyChanged(nameof(CanScanRepository));
        ScanRepositoryCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanScanRepository));
        ScanRepositoryCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedInstallTypeChanged(AgentSkillInstallType? value) =>
        OnPropertyChanged(nameof(InstallTargetDirectory));

    partial void OnCustomInstallDirectoryChanged(string value) =>
        OnPropertyChanged(nameof(InstallTargetDirectory));

    partial void OnInstallSearchTextChanged(string value) => RefreshVisibleInstallCandidates();

    partial void OnSelectAllInstallCandidatesChanged(bool value)
    {
        if (_synchronizingInstallSelection)
            return;

        _synchronizingInstallSelection = true;
        try
        {
            foreach (var candidate in InstallCandidates)
                candidate.IsSelected = value;
        }
        finally
        {
            _synchronizingInstallSelection = false;
        }

        OnPropertyChanged(nameof(HasSelectedInstallCandidates));
    }

    private void OnInstallCandidateSelectionChanged()
    {
        if (!_synchronizingInstallSelection)
        {
            _synchronizingInstallSelection = true;
            try
            {
                SelectAllInstallCandidates = InstallCandidates.Count > 0 && InstallCandidates.All(candidate => candidate.IsSelected);
            }
            finally
            {
                _synchronizingInstallSelection = false;
            }
        }

        OnPropertyChanged(nameof(HasSelectedInstallCandidates));
    }

    private void RefreshVisibleInstallCandidates()
    {
        var searchText = InstallSearchText.Trim();
        var candidates = string.IsNullOrEmpty(searchText)
            ? InstallCandidates
            : InstallCandidates.Where(candidate => candidate.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase));

        VisibleInstallCandidates.Clear();
        foreach (var candidate in candidates)
            VisibleInstallCandidates.Add(candidate);
    }

    public override void Dispose()
    {
        _scanCancellation?.Cancel();
        DisposeRepositoryScan();
        base.Dispose();
    }

    private void DisposeRepositoryScan()
    {
        _repositoryScan?.Dispose();
        _repositoryScan = null;
    }
}

public sealed class AgentSkillSourceGroupViewModel(
    string source,
    string sourcePath,
    IEnumerable<AgentSkillItemViewModel> skills,
    string? displaySource = null)
{
    public string Source { get; } = source;
    public string DisplaySource { get; } = displaySource ?? source;
    public string SourcePath { get; } = sourcePath;
    public ObservableCollection<AgentSkillItemViewModel> Skills { get; } = new(skills);
}

public sealed partial class AgentSkillItemViewModel : ObservableObject
{
    private readonly Func<string, bool, Task> _setEnabled;
    private bool _ready;

    public AgentSkillItemViewModel(AgentSkillDefinition skill, string enabledText, string scriptsText, Func<string, bool, Task> setEnabled)
    {
        Definition = skill;
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
    public AgentSkillDefinition Definition { get; }
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
    public bool HasInstallMetadata => Definition.InstallMetadata is not null;

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

public sealed partial class AgentSkillInstallCandidateViewModel : ObservableObject
{
    private readonly Action _selectionChanged;

    public AgentSkillInstallCandidateViewModel(AgentSkillInstallCandidate candidate, Action selectionChanged)
    {
        ArgumentNullException.ThrowIfNull(selectionChanged);
        Id = candidate.Id;
        Name = candidate.Name;
        Description = candidate.Description;
        _selectionChanged = selectionChanged;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }

    [ObservableProperty] private bool _isSelected = true;

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}
