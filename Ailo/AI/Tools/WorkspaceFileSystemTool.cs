using System.ComponentModel;
using System.Text;

namespace Ailo.AI.Tools;

/// <summary>Text-file and directory operations constrained to the current chat workspace.</summary>
public sealed class WorkspaceFileSystemTool(ChatWorkspace workspace)
{
    private const int MaximumReadBytes = 1_048_576;
    private const int MaximumWriteCharacters = 1_048_576;
    private const int MaximumDirectoryEntries = 500;

    [Description("Lists the files and folders explicitly selected as the current chat workspace. Call this before other file tools to obtain authorized absolute paths.")]
    public string GetWorkspaceEntries()
    {
        var entries = workspace.Entries;
        return entries.Count == 0
            ? "No workspace files or folders are selected."
            : string.Join('\n', entries.Select(static entry => $"{(entry.IsDirectory ? "folder" : "file")}\t{entry.Path}"));
    }

    [Description("Reads a UTF-8 text file inside the selected workspace. The path must be an absolute path returned by get_workspace_entries or contained in a selected folder.")]
    public async Task<string> ReadFileAsync(
        [Description("Authorized absolute file path.")] string path,
        CancellationToken cancellationToken = default)
    {
        var authorizedPath = WorkspacePathSecurity.Authorize(workspace, path);
        var info = new FileInfo(authorizedPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The requested workspace file does not exist.", authorizedPath);
        }

        if (info.Length > MaximumReadBytes)
        {
            throw new InvalidOperationException($"The file is larger than the {MaximumReadBytes} byte read limit.");
        }

        await using var stream = new FileStream(authorizedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    [Description("Writes complete UTF-8 text content to a file inside the selected workspace. Existing files are replaced. New files may only be created inside a selected folder.")]
    public async Task<string> WriteFileAsync(
        [Description("Authorized absolute file path.")] string path,
        [Description("Complete text content to write.")] string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > MaximumWriteCharacters)
        {
            throw new InvalidOperationException($"Content exceeds the {MaximumWriteCharacters} character write limit.");
        }

        var authorizedPath = WorkspacePathSecurity.Authorize(workspace, path, allowMissingLeaf: true);
        var fileAlreadyExists = File.Exists(authorizedPath);
        var parent = Path.GetDirectoryName(authorizedPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException("The parent directory must already exist. Use create_workspace_directory first.");
        }

        // Re-authorize the physical parent immediately before opening the file so an existing
        // symlink cannot redirect a write outside the selected workspace.
        if (!fileAlreadyExists)
        {
            WorkspacePathSecurity.Authorize(workspace, parent);
        }
        var previousContent = fileAlreadyExists
            ? await File.ReadAllTextAsync(authorizedPath, cancellationToken).ConfigureAwait(false)
            : string.Empty;
        var changes = TextLineChangeSummary.Calculate(previousContent, content);
        await File.WriteAllTextAsync(authorizedPath, content, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
        var action = fileAlreadyExists ? "Updated file" : "Created file";
        return $"{action}: {authorizedPath} (added {changes.AddedLines} lines, removed {changes.RemovedLines} lines).";
    }

    [Description("Creates a directory, including missing parent directories, inside a selected workspace folder.")]
    public string CreateDirectory(
        [Description("Authorized absolute directory path.")] string path)
    {
        var authorizedPath = WorkspacePathSecurity.Authorize(workspace, path, allowMissingLeaf: true);
        Directory.CreateDirectory(authorizedPath);
        // Resolve and re-authorize after creation to catch any link changed concurrently.
        WorkspacePathSecurity.Authorize(workspace, authorizedPath);
        return $"Created directory {authorizedPath}.";
    }

    [Description("Lists a directory inside a selected workspace folder. Returns relative child paths and entry types; recursive listing is capped at 500 entries.")]
    public string ListDirectory(
        [Description("Authorized absolute directory path.")] string path,
        [Description("Whether to include all descendants.")] bool recursive = false)
    {
        var authorizedPath = WorkspacePathSecurity.Authorize(workspace, path);
        if (!Directory.Exists(authorizedPath))
        {
            throw new DirectoryNotFoundException($"Workspace directory '{authorizedPath}' does not exist.");
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var entries = Directory.EnumerateFileSystemEntries(authorizedPath, "*", options)
            .Take(MaximumDirectoryEntries + 1)
            .ToArray();
        var truncated = entries.Length > MaximumDirectoryEntries;
        var lines = entries.Take(MaximumDirectoryEntries).Select(entry =>
            $"{(Directory.Exists(entry) ? "folder" : "file")}\t{Path.GetRelativePath(authorizedPath, entry)}");
        var result = string.Join('\n', lines);
        return truncated ? result + "\n[Listing truncated at 500 entries]" : result;
    }
}

internal readonly record struct TextLineChangeSummary(int AddedLines, int RemovedLines)
{
    /// <summary>
    /// Calculates a compact line change summary by retaining the shared prefix and suffix.
    /// File tools replace complete content, so this accurately represents the changed block
    /// without an unbounded quadratic diff for large files.
    /// </summary>
    public static TextLineChangeSummary Calculate(string previous, string next)
    {
        var before = SplitLines(previous);
        var after = SplitLines(next);
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix &&
               before[^(suffix + 1)] == after[^(suffix + 1)])
        {
            suffix++;
        }

        return new TextLineChangeSummary(after.Length - prefix - suffix, before.Length - prefix - suffix);
    }

    private static string[] SplitLines(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .TrimEnd('\n');
        return normalized.Length == 0 ? [] : normalized.Split('\n');
    }
}
