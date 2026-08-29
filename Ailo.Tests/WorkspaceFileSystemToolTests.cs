using Ailo.AI.Tools;

namespace Ailo.Tests;

public sealed class WorkspaceFileSystemToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Ailo.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SelectedFolder_AllowsListingReadingWritingAndDirectoryCreation()
    {
        Directory.CreateDirectory(_root);
        var existing = Path.Combine(_root, "existing.txt");
        await File.WriteAllTextAsync(existing, "before");
        var workspace = new ChatWorkspace();
        workspace.Replace([new WorkspaceEntry(_root, IsDirectory: true)]);
        var tool = new WorkspaceFileSystemTool(workspace);

        Assert.Contains($"folder\t{workspace.Entries[0].Path}", tool.GetWorkspaceEntries());
        Assert.Equal("before", await tool.ReadFileAsync(existing));

        var childDirectory = Path.Combine(_root, "nested");
        tool.CreateDirectory(childDirectory);
        var created = Path.Combine(childDirectory, "created.txt");
        var writeResult = await tool.WriteFileAsync(created, "after");

        Assert.Equal("after", await File.ReadAllTextAsync(created));
        Assert.Contains("added 1 lines, removed 0 lines", writeResult);
        var listing = tool.ListDirectory(_root, recursive: true);
        Assert.Contains("file\texisting.txt", listing);
        Assert.Contains(Path.Combine("nested", "created.txt"), listing);
    }

    [Fact]
    public async Task SelectedFile_DoesNotGrantSiblingOrNewFileAccess()
    {
        Directory.CreateDirectory(_root);
        var selected = Path.Combine(_root, "selected.txt");
        var sibling = Path.Combine(_root, "sibling.txt");
        await File.WriteAllTextAsync(selected, "selected");
        await File.WriteAllTextAsync(sibling, "sibling");
        var workspace = new ChatWorkspace();
        workspace.Replace([new WorkspaceEntry(selected, IsDirectory: false)]);
        var tool = new WorkspaceFileSystemTool(workspace);

        Assert.Equal("selected", await tool.ReadFileAsync(selected));
        await tool.WriteFileAsync(selected, "updated");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tool.ReadFileAsync(sibling));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tool.WriteFileAsync(Path.Combine(_root, "new.txt"), "blocked"));
        Assert.False(workspace.HasWorkspaceDirectory);
        Assert.Null(workspace.WorkspaceDirectory);
    }

    [Fact]
    public void SelectedFolder_EnablesWorkspaceShellScope()
    {
        Directory.CreateDirectory(_root);
        var workspace = new ChatWorkspace();
        workspace.Replace([new WorkspaceEntry(_root, IsDirectory: true)]);

        Assert.True(workspace.HasWorkspaceDirectory);
        Assert.Equal(workspace.Entries[0].Path, workspace.WorkspaceDirectory);
    }

    [Fact]
    public async Task SelectedFolder_RejectsParentTraversal()
    {
        var selectedFolder = Path.Combine(_root, "selected");
        Directory.CreateDirectory(selectedFolder);
        var outside = Path.Combine(_root, "outside.txt");
        await File.WriteAllTextAsync(outside, "secret");
        var workspace = new ChatWorkspace();
        workspace.Replace([new WorkspaceEntry(selectedFolder, IsDirectory: true)]);
        var tool = new WorkspaceFileSystemTool(workspace);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tool.ReadFileAsync(Path.Combine(selectedFolder, "..", "outside.txt")));
    }

    [Fact]
    public async Task SelectedFolder_RejectsSymbolicLinkEscapingWorkspace()
    {
        if (OperatingSystem.IsWindows()) return;

        var selectedFolder = Path.Combine(_root, "selected");
        var outsideFolder = Path.Combine(_root, "outside");
        Directory.CreateDirectory(selectedFolder);
        Directory.CreateDirectory(outsideFolder);
        var outside = Path.Combine(outsideFolder, "secret.txt");
        await File.WriteAllTextAsync(outside, "secret");
        Directory.CreateSymbolicLink(Path.Combine(selectedFolder, "escape"), outsideFolder);
        var workspace = new ChatWorkspace();
        workspace.Replace([new WorkspaceEntry(selectedFolder, IsDirectory: true)]);
        var tool = new WorkspaceFileSystemTool(workspace);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tool.ReadFileAsync(Path.Combine(selectedFolder, "escape", "secret.txt")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            tool.WriteFileAsync(Path.Combine(selectedFolder, "escape", "new.txt"), "blocked"));
    }

    [Fact]
    public void RelativePaths_AreRejected()
    {
        Directory.CreateDirectory(_root);
        var workspace = new ChatWorkspace();
        workspace.Replace([new WorkspaceEntry(_root, IsDirectory: true)]);
        var tool = new WorkspaceFileSystemTool(workspace);

        Assert.Throws<UnauthorizedAccessException>(() => tool.ListDirectory("."));
    }

    [Fact]
    public async Task WriteFileAsync_ReportsAddedAndRemovedLines()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "changes.txt");
        await File.WriteAllTextAsync(path, "keep\nold\nend\n");
        var workspace = new ChatWorkspace();
        workspace.Replace([new WorkspaceEntry(_root, IsDirectory: true)]);
        var tool = new WorkspaceFileSystemTool(workspace);

        var result = await tool.WriteFileAsync(path, "keep\nnew\nmore\nend\n");

        Assert.Contains("added 2 lines, removed 1 lines", result);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best effort test cleanup.
        }
    }
}
