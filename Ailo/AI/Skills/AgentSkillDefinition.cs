namespace Ailo.AI.Skills;

/// <summary>A file-based Agent Skill discovered from one of the configured local sources.</summary>
public sealed record AgentSkillDefinition(
    string Id,
    string Source,
    string SourceRoot,
    string DirectoryPath,
    string Name,
    string Description,
    bool HasScripts,
    bool IsEnabled,
    DateTimeOffset LastSeenAt,
    DateTimeOffset UpdatedAt);

/// <summary>One local directory that contributes Agent Skills.</summary>
public sealed record AgentSkillSourceDirectory(string Name, string Path);

/// <summary>One supported destination layout for an installed Agent Skill.</summary>
public sealed record AgentSkillInstallType(string Name, string RelativeDirectory, string DefaultDirectory)
{
    public string GetInstallDirectory(string? customBaseDirectory) => string.IsNullOrWhiteSpace(customBaseDirectory)
        ? DefaultDirectory
        : Path.Combine(Path.GetFullPath(customBaseDirectory), RelativeDirectory);
}

/// <summary>A skill package discovered in a cloned Git repository.</summary>
public sealed record AgentSkillInstallCandidate(string Id, string Name, string Description, string RelativeDirectory);

/// <summary>Current stage while a Git repository is being scanned for skills.</summary>
public enum AgentSkillScanStep
{
    CloningRepository,
    ScanningSkills
}

/// <summary>Temporary cloned repository retained between the scan and install steps.</summary>
public sealed class AgentSkillRepositoryScan : IDisposable
{
    private bool _disposed;

    internal AgentSkillRepositoryScan(
        string repositoryUrl,
        string repositoryPath,
        IReadOnlyList<AgentSkillInstallCandidate> skills)
    {
        RepositoryUrl = repositoryUrl;
        RepositoryPath = repositoryPath;
        Skills = skills;
    }

    public string RepositoryUrl { get; }
    public IReadOnlyList<AgentSkillInstallCandidate> Skills { get; }
    internal string RepositoryPath { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            if (Directory.Exists(RepositoryPath))
                Directory.Delete(RepositoryPath, recursive: true);
        }
        catch (IOException)
        {
            // Temporary scan data is best-effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Temporary scan data is best-effort cleanup.
        }
    }
}
