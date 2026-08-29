namespace Ailo.ViewModels;

public sealed class WorkspaceEntryViewModel(string path, bool isDirectory) : ViewModelBase
{
    public string Path { get; } = path;
    public bool IsDirectory { get; } = isDirectory;
    public bool IsFile => !IsDirectory;
    public string DisplayName { get; } = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name
        ? name
        : path;
}
