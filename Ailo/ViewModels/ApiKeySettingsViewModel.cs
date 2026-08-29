using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ailo.AI.Providers;
using Ailo.Localization;
using Ailo.Logging;
using Ailo.Services;

namespace Ailo.ViewModels;

public sealed partial class ApiKeySettingsViewModel : SettingsViewModelBase
{
    private readonly ProviderService _providerService;
    private readonly AppState _appState;
    private readonly IConfirmationService? _confirmation;

    public ApiKeySettingsViewModel(
        ProviderService providerService,
        AppState appState,
        LocalizationService localization,
        IConfirmationService? confirmation = null)
        : base(localization)
    {
        _providerService = providerService;
        _appState = appState;
        _confirmation = confirmation;
    }

    public ObservableCollection<ApiProvider> Providers { get; } = [];
    public IReadOnlyList<ProviderType> ProviderTypes { get; } = Enum.GetValues<ProviderType>();
    public ObservableCollection<string> FetchedModels { get; } = [];

    [ObservableProperty] private ApiProvider? _selectedProvider;

    [ObservableProperty] private string _providerName = string.Empty;

    [ObservableProperty] private ProviderType _providerType = ProviderType.OpenAi;

    [ObservableProperty] private string _providerEndpoint = string.Empty;

    public ObservableCollection<ProviderModelItem> ProviderModels { get; } = [];

    [ObservableProperty] private string _providerApiKey = string.Empty;

    [ObservableProperty] private bool _isFetchingModels;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Providers.Clear();
        foreach (var provider in await _providerService.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (provider.IsEnabled)
            {
                Providers.Add(provider);
            }
        }

        if (SelectedProvider is null && Providers.Count > 0)
        {
            SelectedProvider = Providers.FirstOrDefault(p => p.IsDefault) ?? Providers[0];
        }
    }

    [RelayCommand]
    private void AddModel() => ProviderModels.Add(new ProviderModelItem());

    [RelayCommand]
    private void RemoveModel(ProviderModelItem? item)
    {
        if (item is not null) ProviderModels.Remove(item);
    }

    [RelayCommand]
    private void AddProvider()
    {
        SelectedProvider = null;
        ProviderName = string.Empty;
        ProviderType = ProviderType.OpenAi;
        ProviderModels.Clear();
        ProviderModels.Add(new ProviderModelItem());
        ProviderEndpoint = string.Empty;
        ProviderApiKey = string.Empty;
        FetchedModels.Clear();
        StatusMessage = T("AddingProvider");
    }

    [RelayCommand]
    private async Task SaveProviderAsync()
    {
        var provider = BuildEditedProvider();
        try
        {
            await _providerService.SaveAsync(provider).ConfigureAwait(false);
            await LoadAsync().ConfigureAwait(false);
            SelectedProvider = Providers.FirstOrDefault(item => item.Id == provider.Id);
            StatusMessage = T("ProviderSaved");
            _ = _appState.ReloadAiProvidersAsync();
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ApiKeySettingsViewModel), "Failed to save provider");
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteProviderAsync(ApiProvider? provider)
    {
        if (provider is null) return;

        if (_confirmation is not null && !await _confirmation.ConfirmDeleteAsync(provider.Name))
            return;

        try
        {
            await _providerService.DeleteAsync(provider.Id).ConfigureAwait(false);
            await LoadAsync().ConfigureAwait(false);
            StatusMessage = T("ProviderDeleted");
            _ = _appState.ReloadAiProvidersAsync();
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ApiKeySettingsViewModel), "Failed to delete provider");
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task FetchModelsAsync()
    {
        if (IsFetchingModels) return;
        IsFetchingModels = true;
        try
        {
            var provider = BuildEditedProvider();
            if (provider.ProviderType == ProviderType.Anthropic)
            {
                StatusMessage = T("FetchModelsNotSupported");
                return;
            }

            StatusMessage = T("FetchingModels");
            FetchedModels.Clear();
            var models = await _providerService.FetchModelsAsync(provider).ConfigureAwait(false);
            foreach (var model in models)
                FetchedModels.Add(model);

            StatusMessage = string.Format(T("FetchedModelsCount"), FetchedModels.Count);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ApiKeySettingsViewModel), "Failed to fetch models");
            StatusMessage = exception.Message;
        }
        finally
        {
            IsFetchingModels = false;
        }
    }

    [RelayCommand]
    private void AddFetchedModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return;
        var trimmed = modelId.Trim();
        if (ProviderModels.Any(item => string.Equals(item.ModelId, trimmed, StringComparison.OrdinalIgnoreCase))) return;
        ProviderModels.Add(new ProviderModelItem(trimmed, false));
    }

    /// <summary>Projects editable model rows into the model ID list and the multimodal model ID set.</summary>
    public static (string[] ModelIds, IReadOnlySet<string> MultimodalModelIds) BuildModelLists(IEnumerable<ProviderModelItem> items)
    {
        var rows = items
            .Select(item => (ModelId: item.ModelId.Trim(), item.IsMultimodal))
            .Where(row => row.ModelId.Length > 0)
            .ToList();
        var modelIds = rows.Select(row => row.ModelId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var multimodal = rows
            .Where(row => row.IsMultimodal)
            .Select(row => row.ModelId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (modelIds, multimodal);
    }

    private ApiProvider BuildEditedProvider()
    {
        var existing = SelectedProvider;
        var now = DateTimeOffset.UtcNow;
        var (modelIds, multimodalModelIds) = BuildModelLists(ProviderModels);
        var activeModel = modelIds.FirstOrDefault() ?? string.Empty;
        return new ApiProvider(
                existing?.Id ?? Guid.NewGuid().ToString("N"),
                string.IsNullOrWhiteSpace(ProviderName) ? (existing?.Name ?? "Unnamed") : ProviderName.Trim(),
                ProviderType,
                string.IsNullOrWhiteSpace(ProviderApiKey) ? existing?.ApiKey ?? string.Empty : ProviderApiKey,
                string.IsNullOrWhiteSpace(ProviderEndpoint) ? null : ProviderEndpoint.Trim(),
                activeModel,
                existing?.IsDefault ?? Providers.Count == 0,
                true,
                existing?.CreatedAt ?? now,
                now) with
            {
                ModelIds = modelIds,
                MultimodalModelIds = multimodalModelIds
            };
    }

    partial void OnSelectedProviderChanged(ApiProvider? value)
    {
        FetchedModels.Clear();
        ProviderModels.Clear();
        if (value is null) return;

        ProviderName = value.Name;
        ProviderType = value.ProviderType;
        foreach (var model in value.ModelIds)
        {
            ProviderModels.Add(new ProviderModelItem(model, value.MultimodalModelIds.Contains(model, StringComparer.OrdinalIgnoreCase)));
        }
        ProviderEndpoint = value.Endpoint ?? string.Empty;
        ProviderApiKey = string.Empty;
    }
}