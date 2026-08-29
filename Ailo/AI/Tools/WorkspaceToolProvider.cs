using Microsoft.Extensions.AI;

namespace Ailo.AI.Tools;

public sealed class WorkspaceToolProvider(WorkspaceFileSystemTool fileSystem) : IChatToolProvider
{
    public Task<IEnumerable<ChatToolRegistration>> GetTools()
    {
        ChatToolRegistration[] registrations =
        [
            Create("get_workspace_entries", fileSystem.GetWorkspaceEntries, "Getting workspace entries..."),
            Create("read_workspace_file", fileSystem.ReadFileAsync, "Reading file..."),
            Create("write_workspace_file", fileSystem.WriteFileAsync, "Writing file..."),
            Create("create_workspace_directory", fileSystem.CreateDirectory, "Creating directory..."),
            Create("list_workspace_directory", fileSystem.ListDirectory, "Listing directory contents...")
        ];
        return Task.FromResult<IEnumerable<ChatToolRegistration>>(registrations);
    }

    private static ChatToolRegistration Create(string name, Delegate implementation, string fallbackNotice) =>
        new(name,
            AIFunctionFactory.Create(implementation, new AIFunctionFactoryOptions { Name = name }),
            call => FormatPathNotice(call, fallbackNotice));

    private static string FormatPathNotice(FunctionCallContent call, string fallback)
    {
        if (call.Arguments is not null && call.Arguments.TryGetValue("path", out var path) &&
            path?.ToString() is { Length: > 0 } pathText)
        {
            return $"{fallback.TrimEnd('…')}：{pathText}";
        }

        return fallback;
    }
}
