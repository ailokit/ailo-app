using Ailo.Logging;
using Ailo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Agents.AI.Tools.Shell;

namespace Ailo.AI.Tools;

/// <summary>
/// Process-wide shell selection and Docker/Podman capability state.
/// The selected backend is persisted separately from the per-chat workspace.
/// </summary>
public sealed partial class ShellToolConfiguration : ObservableObject
{
    public const string SettingKey = AppSettingsService.ShellToolKey;
    public const string EnabledSettingKey = AppSettingsService.ShellToolEnabledKey;
    public const string LocalValue = "local";
    public const string DockerValue = "docker";

    private readonly AppSettingsService _settings;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    [ObservableProperty]
    private ShellToolKind _selectedTool = ShellToolKind.Local;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isDockerShellAvailable;

    [ObservableProperty]
    private string? _containerRuntimeBinary;

    public ShellToolConfiguration(AppSettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>Loads the saved selection and probes Docker/Podman once at startup.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var runtime = await ProbeContainerRuntimeAsync(cancellationToken).ConfigureAwait(false);
            ContainerRuntimeBinary = runtime.Binary;
            IsDockerShellAvailable = runtime.IsAvailable;

            var saved = await _settings.GetAsync(SettingKey, cancellationToken).ConfigureAwait(false);
            var selected = string.Equals(saved, DockerValue, StringComparison.OrdinalIgnoreCase)
                ? ShellToolKind.Docker
                : ShellToolKind.Local;
            SelectedTool = selected == ShellToolKind.Docker && !IsDockerShellAvailable
                ? ShellToolKind.Local
                : selected;

            var savedEnabled = await _settings.GetAsync(EnabledSettingKey, cancellationToken).ConfigureAwait(false);
            IsEnabled = !bool.TryParse(savedEnabled, out var enabled) || enabled;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    /// <summary>Changes the backend when it is available and persists the choice.</summary>
    public bool TrySelect(ShellToolKind tool)
    {
        if (tool == ShellToolKind.Docker && !IsDockerShellAvailable)
            return false;

        if (SelectedTool == tool)
            return true;

        SelectedTool = tool;
        _ = PersistSelectionAsync(tool);
        return true;
    }

    /// <summary>Enables or disables shell execution and persists the choice.</summary>
    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled)
            return;

        IsEnabled = enabled;
        _ = PersistEnabledAsync(enabled);
    }

    private async Task PersistSelectionAsync(ShellToolKind tool)
    {
        try
        {
            await _settings.SaveAsync(SettingKey, tool == ShellToolKind.Docker ? DockerValue : LocalValue)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ShellToolConfiguration), "Failed to persist shell tool selection");
        }
    }

    private async Task PersistEnabledAsync(bool enabled)
    {
        try
        {
            await _settings.SaveAsync(EnabledSettingKey, enabled ? "true" : "false").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ShellToolConfiguration), "Failed to persist shell tool enabled state");
        }
    }

    private static async Task<(bool IsAvailable, string? Binary)> ProbeContainerRuntimeAsync(
        CancellationToken cancellationToken)
    {
        foreach (var binary in new[] { "docker", "podman" })
        {
            try
            {
                if (await DockerShellExecutor.IsAvailableAsync(binary, cancellationToken).ConfigureAwait(false))
                    return (true, binary);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // A missing binary, stopped daemon, or unavailable OCI runtime makes this
                // candidate unusable; Podman is still worth probing after Docker fails.
            }
        }

        return (false, null);
    }
}
