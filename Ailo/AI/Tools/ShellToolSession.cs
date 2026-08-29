using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;

namespace Ailo.AI.Tools;

/// <summary>
/// Owns one persistent shell executor and its environment context for one conversation.
/// </summary>
public sealed class ShellToolSession : IAsyncDisposable
{
    private ShellToolSession(
        ShellToolKind kind,
        string hostWorkspaceDirectory,
        ShellExecutor executor,
        ShellEnvironmentProvider environmentProvider,
        AITool tool,
        ChatToolRegistration registration)
    {
        Kind = kind;
        HostWorkspaceDirectory = hostWorkspaceDirectory;
        Executor = executor;
        EnvironmentProvider = environmentProvider;
        Tool = tool;
        Registration = registration;
    }

    public ShellToolKind Kind { get; }
    public string HostWorkspaceDirectory { get; }
    public ShellExecutor Executor { get; }
    public ShellEnvironmentProvider EnvironmentProvider { get; }
    public AITool Tool { get; }
    public ChatToolRegistration Registration { get; }

    public static async Task<ShellToolSession> CreateAsync(
        ShellToolKind kind,
        string hostWorkspaceDirectory,
        string? dockerBinary,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostWorkspaceDirectory);
        if (!Directory.Exists(hostWorkspaceDirectory))
            throw new DirectoryNotFoundException($"Workspace folder '{hostWorkspaceDirectory}' does not exist.");

        ShellExecutor executor;
        switch (kind)
        {
            case ShellToolKind.Docker:
                if (string.IsNullOrWhiteSpace(dockerBinary))
                    throw new InvalidOperationException("No Docker or Podman OCI runtime is available.");

                executor = new DockerShellExecutor(new DockerShellExecutorOptions
                {
                    DockerBinary = dockerBinary,
                    HostWorkdir = hostWorkspaceDirectory,
                    MountReadonly = false,
                    Mode = ShellMode.Persistent,
                    Timeout = DockerShellExecutor.DefaultTimeout,
                    ReadOnlyRoot = true,
                    Network = DockerNetworkMode.None
                });
                break;

            default:
                executor = new LocalShellExecutor(new LocalShellExecutorOptions
                {
                    WorkingDirectory = hostWorkspaceDirectory,
                    ConfineWorkingDirectory = true,
                    Mode = ShellMode.Persistent,
                    Timeout = LocalShellExecutor.DefaultTimeout,
                    AcknowledgeUnsafe = true
                });
                break;
        }

        try
        {
            await executor.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var environmentProvider = new ShellEnvironmentProvider(executor);
            var tool = executor.AsAIFunction(
                "run_shell",
                "Runs a shell command in the selected workspace folder. Use it for repository inspection, builds, tests, and file operations within the workspace.",
                requireApproval: false);
            var registration = new ChatToolRegistration(
                "run_shell",
                tool,
                static call => call.Arguments is not null &&
                    call.Arguments.TryGetValue("command", out var command) &&
                    command?.ToString() is { Length: > 0 } commandText
                        ? $"Running command: {commandText}"
                        : "Running shell command...");

            return new ShellToolSession(kind, hostWorkspaceDirectory, executor, environmentProvider, tool, registration);
        }
        catch
        {
            await executor.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask DisposeAsync() => Executor.DisposeAsync();
}
