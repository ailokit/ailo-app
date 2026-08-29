using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using Ailo.AI.Mcp;
using Ailo.Localization;
using Ailo.Logging;
using Ailo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ailo.ViewModels;

public sealed partial class McpServerItem : ObservableObject
{
    public McpServerItem(McpServer server)
    {
        Id = server.Id;
        Name = server.Name;
        Transport = server.Transport;
        IsEnabled = server.IsEnabled;
        CreatedAt = server.CreatedAt;
    }

    public string Id { get; }
    public DateTimeOffset CreatedAt { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private McpTransportKind _transport;
    [ObservableProperty] private bool _isEnabled;
}

public sealed partial class McpToolItem : ObservableObject
{
    public McpToolItem(McpTool tool)
    {
        Id = tool.Id;
        Name = tool.Name;
        Description = tool.Description ?? string.Empty;
        IsEnabled = tool.IsEnabled;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }

    [ObservableProperty] private bool _isEnabled;
}

public sealed partial class McpSettingsViewModel : SettingsViewModelBase
{
    private readonly McpServerRepository _servers;
    private readonly McpClientService _client;
    private readonly IConfirmationService? _confirmation;

    public McpSettingsViewModel(
        McpServerRepository servers,
        McpClientService client,
        LocalizationService localization,
        IConfirmationService? confirmation = null)
        : base(localization)
    {
        _servers = servers;
        _client = client;
        _confirmation = confirmation;
    }

    public ObservableCollection<McpServerItem> Servers { get; } = [];
    public ObservableCollection<McpToolItem> Tools { get; } = [];
    public IReadOnlyList<McpTransportKind> TransportKinds { get; } = Enum.GetValues<McpTransportKind>();
    public bool IsHttpTransport => Transport == McpTransportKind.StreamableHttp;
    public bool IsStdioTransport => Transport == McpTransportKind.Stdio;

    [ObservableProperty] private McpServerItem? _selectedServer;
    [ObservableProperty] private McpTransportKind _transport = McpTransportKind.StreamableHttp;
    [ObservableProperty] private string _serverName = string.Empty;
    [ObservableProperty] private string _endpoint = string.Empty;
    [ObservableProperty] private string _command = string.Empty;
    [ObservableProperty] private string _argumentsJson = "[]";
    [ObservableProperty] private string _environmentJson = "{}";
    [ObservableProperty] private string _headersJson = "{}";
    [ObservableProperty] private bool _serverEnabled = true;
    [ObservableProperty] private bool _isRefreshing;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        foreach (var server in Servers)
        {
            server.PropertyChanged -= OnServerPropertyChanged;
        }

        Servers.Clear();
        foreach (var server in await _servers.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            AddServerItem(server);
        }

        SelectedServer ??= Servers.FirstOrDefault();
        if (SelectedServer is not null)
        {
            await LoadToolsAsync(SelectedServer.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void AddMcpServer()
    {
        var item = new McpServerItem(new McpServer(
            Guid.NewGuid().ToString("N"), T("NewMcpServer"), McpTransportKind.StreamableHttp,
            null, string.Empty, "[]", "{}", "{}", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        AddServerItem(item);
        SelectedServer = item;
        StatusMessage = T("AddingMcpServer");
    }

    [RelayCommand]
    private async Task SaveMcpServerAsync()
    {
        if (SelectedServer is null)
        {
            AddMcpServer();
        }

        if (SelectedServer is null || string.IsNullOrWhiteSpace(ServerName))
        {
            StatusMessage = T("McpServerNameRequired");
            return;
        }

        try
        {
            ValidateJson(ArgumentsJson, JsonValueKind.Array);
            ValidateJson(EnvironmentJson, JsonValueKind.Object);
            ValidateJson(HeadersJson, JsonValueKind.Object);
            var now = DateTimeOffset.UtcNow;
            var server = new McpServer(
                SelectedServer.Id,
                ServerName.Trim(),
                Transport,
                string.IsNullOrWhiteSpace(Endpoint) ? null : Endpoint.Trim(),
                string.IsNullOrWhiteSpace(Command) ? null : Command.Trim(),
                ArgumentsJson.Trim(), EnvironmentJson.Trim(), HeadersJson.Trim(), ServerEnabled,
                SelectedServer.CreatedAt, now);
            await _servers.SaveAsync(server).ConfigureAwait(false);
            await LoadAsync().ConfigureAwait(false);
            SelectedServer = Servers.FirstOrDefault(item => item.Id == server.Id);
            StatusMessage = T("McpServerSaved");
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(McpSettingsViewModel), "Failed to save MCP server");
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteMcpServerAsync(McpServerItem? server)
    {
        if (server is null) return;
        if (_confirmation is not null && !await _confirmation.ConfirmDeleteAsync(server.Name).ConfigureAwait(false)) return;

        try
        {
            await _servers.DeleteAsync(server.Id).ConfigureAwait(false);
            await LoadAsync().ConfigureAwait(false);
            StatusMessage = T("McpServerDeleted");
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(McpSettingsViewModel), "Failed to delete MCP server");
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshMcpToolsAsync()
    {
        if (SelectedServer is null || IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            StatusMessage = T("RefreshingMcpTools");
            await _client.RefreshToolsAsync(ToServer(SelectedServer)).ConfigureAwait(false);
            await LoadToolsAsync(SelectedServer.Id).ConfigureAwait(false);
            StatusMessage = string.Format(T("McpToolsRefreshed"), Tools.Count);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(McpSettingsViewModel), "Failed to refresh MCP tools");
            StatusMessage = exception.Message;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void AddServerItem(McpServer server)
    {
        var item = new McpServerItem(server);
        AddServerItem(item);
    }

    private void AddServerItem(McpServerItem item)
    {
        item.PropertyChanged += OnServerPropertyChanged;
        Servers.Add(item);
    }

    private async void OnServerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(McpServerItem.IsEnabled) || sender is not McpServerItem item)
            return;

        if (SelectedServer?.Id == item.Id)
        {
            ServerEnabled = item.IsEnabled;
        }

        try
        {
            var server = await _servers.GetByIdAsync(item.Id).ConfigureAwait(false);
            if (server is not null)
            {
                await _servers.SaveAsync(server with { IsEnabled = item.IsEnabled, UpdatedAt = DateTimeOffset.UtcNow }).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(McpSettingsViewModel), "Failed to toggle MCP server");
        }
    }

    partial void OnSelectedServerChanged(McpServerItem? value)
    {
        Tools.Clear();
        if (value is null)
        {
            ServerName = string.Empty;
            return;
        }

        ServerName = value.Name;
        Transport = value.Transport;
        ServerEnabled = value.IsEnabled;
        _ = LoadServerDetailsAsync(value.Id);
    }

    partial void OnTransportChanged(McpTransportKind value)
    {
        OnPropertyChanged(nameof(IsHttpTransport));
        OnPropertyChanged(nameof(IsStdioTransport));
    }

    private async Task LoadServerDetailsAsync(string id)
    {
        var server = await _servers.GetByIdAsync(id).ConfigureAwait(false);
        if (server is null || SelectedServer?.Id != id) return;
        Endpoint = server.Endpoint ?? string.Empty;
        Command = server.Command ?? string.Empty;
        ArgumentsJson = server.ArgumentsJson;
        EnvironmentJson = server.EnvironmentJson;
        HeadersJson = server.HeadersJson;
        await LoadToolsAsync(id).ConfigureAwait(false);
    }

    private async Task LoadToolsAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var tools = await _servers.GetToolsAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (SelectedServer?.Id != serverId) return;
        Tools.Clear();
        foreach (var tool in tools)
        {
            var item = new McpToolItem(tool);
            item.PropertyChanged += OnToolPropertyChanged;
            Tools.Add(item);
        }
    }

    private async void OnToolPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(McpToolItem.IsEnabled) || sender is not McpToolItem tool) return;
        try
        {
            await _servers.SetToolEnabledAsync(tool.Id, tool.IsEnabled).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(McpSettingsViewModel), "Failed to toggle MCP tool");
        }
    }

    private McpServer ToServer(McpServerItem item) => new(
        item.Id, ServerName, Transport,
        string.IsNullOrWhiteSpace(Endpoint) ? null : Endpoint.Trim(),
        string.IsNullOrWhiteSpace(Command) ? null : Command.Trim(),
        ArgumentsJson, EnvironmentJson, HeadersJson, ServerEnabled,
        item.CreatedAt, DateTimeOffset.UtcNow);

    private static void ValidateJson(string json, JsonValueKind expected)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != expected)
            throw new InvalidOperationException($"Expected a JSON {expected.ToString().ToLowerInvariant()}.");
    }
}
