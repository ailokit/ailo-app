using System.Collections.ObjectModel;
using Avalonia.Threading;
using Ailo.AI.Conversations;
using Ailo.AI.Providers;
using Ailo.AI.Skills;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Ailo.ViewModels;

public partial class AppState
{
    private ProviderService ProviderService => _lazyServiceProvider.GetRequiredService<ProviderService>();
    private SkillRepository SkillRepository => _lazyServiceProvider.GetRequiredService<SkillRepository>();
    private AgentSkillsService AgentSkillsService => _lazyServiceProvider.GetRequiredService<AgentSkillsService>();

    private ConversationRepository ConversationRepository =>
        _lazyServiceProvider.GetRequiredService<ConversationRepository>();

    /// <summary>
    /// API Providers
    /// </summary>
    [ObservableProperty] private ObservableCollection<ApiProvider> _providers = [];

    [ObservableProperty] private ObservableCollection<Skill> _skills = [];
    [ObservableProperty] private ObservableCollection<Conversation> _conversations = [];

    private async Task LoadAiStateAsync(CancellationToken cancellationToken = default)
    {
        await ReloadAiProvidersAsync(cancellationToken);
        await ReloadAiSkillsAsync(cancellationToken);
        await AgentSkillsService.RefreshAsync(cancellationToken);
    }

    public async Task ReloadAiProvidersAsync(CancellationToken cancellationToken = default)
    {
        Providers.Clear();
        var providers = await ProviderService.GetListAsync(cancellationToken);
        foreach (var provider in providers)
        {
            Providers.Add(provider);
        }
        Dispatcher.UIThread.Post(()=>OnPropertyChanged(nameof(Providers)));
    }

    public async Task ReloadAiSkillsAsync(CancellationToken cancellationToken = default)
    {
        Skills.Clear();
        var skillsData = await SkillRepository.GetEnabledAsync(cancellationToken).ConfigureAwait(false);
        foreach (var skill in skillsData)
        {
            Skills.Add(skill);
        }
    }

    public async Task RefreshConversationHistoryAsync(CancellationToken cancellationToken = default)
    {
        Conversations.Clear();
        var conversations = await ConversationRepository.GetRecentAsync(cancellationToken).ConfigureAwait(false);
        foreach (var conversation in conversations)
        {
            Conversations.Add(conversation);
        }
    }

    public void ClearConversationHistory(CancellationToken cancellationToken = default)
    {
        Conversations?.Clear();
    }
}
