using Avalonia.Controls;
using Avalonia.Input;
using Ailo.ViewModels;
using Ailo.Views.Settings;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;

namespace Ailo.Views;

internal partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel _viewModel;

    protected override bool HideOnClose => false;
    private Control? _currentPage;
    private bool _updatingNavigation;
    private bool _initialized;

    /// <summary>If set before the window is shown, this page will be selected on open.</summary>
    internal string? PendingPageKey { get; set; }

    public SettingsWindow(SettingsWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        ConfigurePlatformTitleBar();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Navigates to a settings page. Used externally when the window is already visible.</summary>
    public void NavigateTo(string pageKey)
    {
        _viewModel.SelectSettingsPage(pageKey);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (!_initialized)
        {
            _initialized = true;
            var pageKey = PendingPageKey ?? _viewModel.SelectedSettingsPageKey;
            _viewModel.SelectSettingsPage(pageKey);
            ShowPage(pageKey);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsWindowViewModel.SelectedSettingsPageKey))
        {
            ShowPage(_viewModel.SelectedSettingsPageKey);
        }
    }

    private void OnSettingsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingNavigation || SettingsNavigation.SelectedItem is not ListBoxItem { Tag: string pageKey })
            return;

        _viewModel.SelectSettingsPage(pageKey);
    }

    private void ShowPage(string pageKey)
    {
        // Scope is set by WindowNavigationService before the window is shown,
        // so it is available by the time OnOpened fires.
        if (Scope is null) return;

        var selectedItem = SettingsNavigation.ItemsView.OfType<ListBoxItem>().FirstOrDefault(item => Equals(item.Tag, pageKey));
        _updatingNavigation = true;
        try { SettingsNavigation.SelectedItem = selectedItem; }
        finally { _updatingNavigation = false; }

        DisposeCurrentPage();

        (_currentPage, var pageVm) = pageKey switch
        {
            SettingsWindowViewModel.GeneralSettingsPage => CreatePage<GeneralSettingsView, GeneralSettingsViewModel>(vm => vm.LoadAsync()),
            SettingsWindowViewModel.ApiKeySettingsPage => CreatePage<ApiKeySettingsView, ApiKeySettingsViewModel>(vm => vm.LoadAsync()),
            SettingsWindowViewModel.SkillsSettingsPage => CreatePage<SkillSettingsView, SkillSettingsViewModel>(vm => vm.LoadAsync()),
            SettingsWindowViewModel.AgentSkillsSettingsPage => CreatePage<AgentSkillsSettingsView, AgentSkillsSettingsViewModel>(vm => vm.LoadAsync()),
            SettingsWindowViewModel.ToolsSettingsPage => CreatePage<ToolSettingsView, ToolSettingsViewModel>(vm => vm.LoadAsync()),
            SettingsWindowViewModel.McpSettingsPage => CreatePage<McpSettingsView, McpSettingsViewModel>(vm => vm.LoadAsync()),
            SettingsWindowViewModel.ShortcutSettingsPage => CreatePage<ShortcutSettingsView, ShortcutSettingsViewModel>(vm => { vm.LoadFromState(); return Task.CompletedTask; }),
            SettingsWindowViewModel.HistorySettingsPage => CreatePage<HistorySettingsView, HistorySettingsViewModel>(_ => Task.CompletedTask),
            SettingsWindowViewModel.ScheduledJobsSettingsPage => CreatePage<ScheduledJobsSettingsView, ScheduledJobsSettingsViewModel>(vm => vm.LoadAsync()),
            SettingsWindowViewModel.AboutSettingsPage => CreatePage<AboutSettingsView, AboutSettingsViewModel>(vm => { vm.LoadFromState(); return Task.CompletedTask; }),
            _ => CreatePage<GeneralSettingsView, GeneralSettingsViewModel>(vm => vm.LoadAsync())
        };

        if (_currentPage is not null)
            SettingsContent.Content = _currentPage;
    }

    private (Control?, SettingsViewModelBase?) CreatePage<TView, TVm>(Func<TVm, Task> initialize)
        where TView : Control, new()
        where TVm : SettingsViewModelBase
    {
        var view = new TView();
        var vm = Scope!.ServiceProvider.GetRequiredService<TVm>();
        if (vm is GeneralSettingsViewModel generalSettings)
        {
            generalSettings.AttachStorageProvider(StorageProvider);
        }
        else if (vm is AgentSkillsSettingsViewModel agentSkillsSettings)
        {
            agentSkillsSettings.AttachStorageProvider(StorageProvider);
            agentSkillsSettings.AttachClipboard(Clipboard);
        }
        view.DataContext = vm;
        _ = initialize(vm);
        return (view, vm);
    }

    private void DisposeCurrentPage()
    {
        if (_currentPage?.DataContext is IDisposable disposable)
            disposable.Dispose();
        SettingsContent.Content = null;
        _currentPage = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        DisposeCurrentPage();
        // The base window disposes the ViewModel (DataContext) and the scope.
        base.OnClosed(e);
    }
}
