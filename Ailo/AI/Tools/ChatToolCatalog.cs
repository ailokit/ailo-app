namespace Ailo.AI.Tools;

/// <summary>Stable user-facing metadata for the tools shipped with Ailo.</summary>
public sealed record ChatToolDefinition(
    string Name,
    string DisplayNameKey,
    string DescriptionKey,
    string CategoryKey = "ToolCategoryOther");

public static class ChatToolCatalog
{
    public static IReadOnlyList<ChatToolDefinition> All { get; } =
    [
        new("fetch_webpage_content", "ToolFetchWebpage", "ToolFetchWebpageDescription", "ToolCategoryWeb"),
        new("open_webpage_in_browser", "ToolOpenWebpage", "ToolOpenWebpageDescription", "ToolCategoryWeb"),
        new("get_workspace_entries", "ToolGetWorkspaceEntries", "ToolGetWorkspaceEntriesDescription", "ToolCategoryWorkspace"),
        new("read_workspace_file", "ToolReadWorkspaceFile", "ToolReadWorkspaceFileDescription", "ToolCategoryWorkspace"),
        new("write_workspace_file", "ToolWriteWorkspaceFile", "ToolWriteWorkspaceFileDescription", "ToolCategoryWorkspace"),
        new("create_workspace_directory", "ToolCreateWorkspaceDirectory", "ToolCreateWorkspaceDirectoryDescription", "ToolCategoryWorkspace"),
        new("list_workspace_directory", "ToolListWorkspaceDirectory", "ToolListWorkspaceDirectoryDescription", "ToolCategoryWorkspace"),
        new("schedule_notification", "ToolScheduleNotification", "ToolScheduleNotificationDescription", "ToolCategoryScheduledJobs"),
        new("show_notification", "ToolShowNotification", "ToolShowNotificationDescription", "ToolCategoryScheduledJobs"),
        new("get_system_information", "ToolGetSystemInformation", "ToolGetSystemInformationDescription", "ToolCategorySystem"),
        new("schedule_agent_job", "ToolScheduleAgentJob", "ToolScheduleAgentJobDescription", "ToolCategoryScheduledJobs"),
        new("list_scheduled_jobs", "ToolListScheduledJobs", "ToolListScheduledJobsDescription", "ToolCategoryScheduledJobs"),
        new("update_scheduled_job", "ToolUpdateScheduledJob", "ToolUpdateScheduledJobDescription", "ToolCategoryScheduledJobs"),
        new("delete_scheduled_job", "ToolDeleteScheduledJob", "ToolDeleteScheduledJobDescription", "ToolCategoryScheduledJobs")
    ];
}
