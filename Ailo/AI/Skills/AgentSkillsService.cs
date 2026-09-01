using System.Security.Cryptography;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ailo.AI;
using Ailo.Services;
using Microsoft.Agents.AI;

namespace Ailo.AI.Skills;

/// <summary>
/// Discovers portable <c>SKILL.md</c> packages and creates the Agent Framework source used by chat and jobs.
/// Each discovered skill directory is supplied separately so a disabled skill cannot be rediscovered through its parent.
/// </summary>
public sealed class AgentSkillsService
{
    private const string InstallMetadataFileName = ".ailo-skill.json";
    private static readonly Regex SkillNamePattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".py", ".js", ".sh", ".ps1", ".cs", ".csx"
    };
    private static readonly IReadOnlyList<string> WorkingDirectorySkillPaths =
    [
        Path.Combine(".ailo", "skills"),
        "skills",
        Path.Combine(".codex", "skills"),
        Path.Combine(".claude", "skills"),
        Path.Combine(".copilot", "skills"),
        Path.Combine(".agents", "skills"),
        Path.Combine(".config", "agents", "skills"),
        Path.Combine(".gemini", "skills"),
        Path.Combine(".opencode", "skills")
    ];

    private readonly string _availabilityPath;
    private readonly AppSettingsService? _settings;
    private readonly IReadOnlyList<AgentSkillSourceDirectory> _sources;
    private readonly IReadOnlyList<AgentSkillInstallType> _installTypes;
    private readonly SemaphoreSlim _availabilityGate = new(1, 1);

    public AgentSkillsService(AppPaths paths)
        : this(paths.SkillsAvailabilityPath, CreateDefaultSources(paths), CreateDefaultInstallTypes(paths), null)
    {
    }

    public AgentSkillsService(AppPaths paths, AppSettingsService settings)
        : this(paths.SkillsAvailabilityPath, CreateDefaultSources(paths), CreateDefaultInstallTypes(paths), settings)
    {
    }

    internal AgentSkillsService(string availabilityPath, IReadOnlyList<AgentSkillSourceDirectory> sources)
        : this(availabilityPath, sources, CreateInstallTypesFromSources(sources), null)
    {
    }

    private AgentSkillsService(
        string availabilityPath,
        IReadOnlyList<AgentSkillSourceDirectory> sources,
        IReadOnlyList<AgentSkillInstallType> installTypes,
        AppSettingsService? settings)
    {
        _availabilityPath = availabilityPath;
        _settings = settings;
        _sources = sources
            .Where(source => !string.IsNullOrWhiteSpace(source.Path))
            .GroupBy(source => Path.GetFullPath(source.Path), PathComparer)
            .Select(group => group.First() with { Path = Path.GetFullPath(group.Key) })
            .ToArray();
        _installTypes = installTypes
            .Where(type => !string.IsNullOrWhiteSpace(type.Name) && !string.IsNullOrWhiteSpace(type.DefaultDirectory))
            .GroupBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with { DefaultDirectory = Path.GetFullPath(group.First().DefaultDirectory) })
            .ToArray();
    }

    public IReadOnlyList<AgentSkillSourceDirectory> Sources => _sources;
    public IReadOnlyList<AgentSkillInstallType> InstallTypes => _installTypes;

    public async Task<IReadOnlyList<AgentSkillDefinition>> RefreshAsync(CancellationToken cancellationToken = default)
        => await RefreshAsync(null, cancellationToken).ConfigureAwait(false);

    /// <summary>Refreshes skills and optionally includes standard skill directories in a working directory.</summary>
    public async Task<IReadOnlyList<AgentSkillDefinition>> RefreshAsync(
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var disabledDirectories = await ReadDisabledDirectoriesAsync(cancellationToken).ConfigureAwait(false);
        var discovered = new List<AgentSkillDefinition>();
        foreach (var source in await GetSourcesAsync(cancellationToken, workingDirectory).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(source.Path))
                continue;

            foreach (var skillFile in EnumerateSkillFiles(source.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frontmatter = ReadFrontmatter(skillFile);
                if (frontmatter is null)
                    continue;

                var directory = Path.GetDirectoryName(skillFile)!;
                discovered.Add(new AgentSkillDefinition(
                    CreateId(directory), source.Name, source.Path, directory, frontmatter.Value.Name,
                    frontmatter.Value.Description, ContainsScript(directory), !disabledDirectories.Contains(directory), now, now,
                    ReadInstallMetadata(directory)));
            }
        }

        return discovered;
    }

    public async Task<AgentFileSkillsSource?> CreateSourceAsync(CancellationToken cancellationToken = default)
        => await CreateSourceAsync(null, cancellationToken).ConfigureAwait(false);

    /// <summary>Creates an enabled skill source, including skills from the supplied working directory.</summary>
    public async Task<AgentFileSkillsSource?> CreateSourceAsync(
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var enabledDirectories = (await RefreshAsync(workingDirectory, cancellationToken).ConfigureAwait(false))
            .Where(skill => skill.IsEnabled)
            .Select(skill => skill.DirectoryPath)
            .Distinct(PathComparer)
            .ToArray();
        return enabledDirectories.Length == 0
            ? null
            : new AgentFileSkillsSource(enabledDirectories, RunSkillScriptAsync);
    }

    /// <summary>All custom skill roots selected by the user, in selection order.</summary>
    public IReadOnlyList<string> CustomSkillsDirectories { get; private set; } = [];

    /// <summary>The most recently selected custom skill root.</summary>
    public string? CustomSkillsDirectory => CustomSkillsDirectories.LastOrDefault();

    public async Task<string?> GetCustomSkillsDirectoryAsync(CancellationToken cancellationToken = default)
    {
        await LoadCustomSkillsDirectoryAsync(cancellationToken).ConfigureAwait(false);
        return CustomSkillsDirectory;
    }

    private async Task<IReadOnlyList<AgentSkillSourceDirectory>> GetSourcesAsync(
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        await LoadCustomSkillsDirectoryAsync(cancellationToken).ConfigureAwait(false);
        var sources = _sources
            .Concat(CustomSkillsDirectories.Select(path => new AgentSkillSourceDirectory("Custom", path)));
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            var normalizedWorkingDirectory = Path.GetFullPath(workingDirectory);
            sources = sources.Concat(WorkingDirectorySkillPaths.Select(relativePath =>
                new AgentSkillSourceDirectory(
                    "Current workspace",
                    Path.Combine(normalizedWorkingDirectory, relativePath))));
        }

        return sources
            .GroupBy(source => Path.GetFullPath(source.Path), PathComparer)
            .Select(group => group.First())
            .ToArray();
    }

    private async Task LoadCustomSkillsDirectoryAsync(CancellationToken cancellationToken)
    {
        if (_settings is null)
            return;

        var savedPaths = await _settings.GetAsync(AppSettingsService.CustomAgentSkillsDirectoriesKey, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(savedPaths))
        {
            // Read the original single-directory setting for existing installations.
            var legacyPath = await _settings.GetAsync(AppSettingsService.CustomAgentSkillsDirectoryKey, cancellationToken)
                .ConfigureAwait(false);
            savedPaths = string.IsNullOrWhiteSpace(legacyPath) ? null : legacyPath;
        }

        var paths = ParseCustomSkillDirectories(savedPaths);
        CustomSkillsDirectories = paths;
    }

    private static IReadOnlyList<string> ParseCustomSkillDirectories(string? savedPaths)
    {
        if (string.IsNullOrWhiteSpace(savedPaths))
            return [];

        string[] paths;
        try
        {
            paths = savedPaths.TrimStart().StartsWith("[", StringComparison.Ordinal)
                ? JsonSerializer.Deserialize(savedPaths, AiloJsonSerializerContext.Default.StringArray) ?? []
                : [savedPaths];
        }
        catch (JsonException)
        {
            return [];
        }

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .ToArray();
    }

    public async Task SetEnabledAsync(string directoryPath, bool isEnabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var normalizedDirectory = Path.GetFullPath(directoryPath);
        await _availabilityGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var disabledDirectories = await ReadDisabledDirectoriesCoreAsync(cancellationToken).ConfigureAwait(false);
            if (isEnabled)
                disabledDirectories.Remove(normalizedDirectory);
            else
                disabledDirectories.Add(normalizedDirectory);
            await WriteDisabledDirectoriesAsync(disabledDirectories, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _availabilityGate.Release();
        }
    }

    /// <summary>
    /// Permanently removes a discovered skill package from its containing directory.
    /// The directory must be a currently discovered skill and cannot be one of the
    /// configured source roots themselves.
    /// </summary>
    public async Task UninstallAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var normalizedDirectory = Path.GetFullPath(directoryPath);
        var skill = (await RefreshAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => PathComparer.Equals(item.DirectoryPath, normalizedDirectory));
        if (skill is null)
            throw new InvalidOperationException("The selected skill is no longer available.");

        var sourceRoot = Path.GetFullPath(skill.SourceRoot);
        var relativePath = Path.GetRelativePath(sourceRoot, normalizedDirectory);
        if (relativePath is "." or ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
            throw new InvalidOperationException("Only a skill package directory can be uninstalled.");

        var directoryInfo = new DirectoryInfo(normalizedDirectory);
        if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Symbolic-link skill directories cannot be uninstalled from Ailo.");

        Directory.Delete(normalizedDirectory, recursive: true);

        // A deleted package no longer needs an availability override. Failure to
        // persist this cleanup is harmless because the package is already gone.
        await _availabilityGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var disabledDirectories = await ReadDisabledDirectoriesCoreAsync(cancellationToken).ConfigureAwait(false);
            if (disabledDirectories.Remove(normalizedDirectory))
                await WriteDisabledDirectoriesAsync(disabledDirectories, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _availabilityGate.Release();
        }
    }

    /// <summary>Clones a Git repository into a temporary directory and scans every folder for skills.</summary>
    public async Task<AgentSkillRepositoryScan> ScanRepositoryAsync(
        string repositoryUrl,
        CancellationToken cancellationToken = default,
        IProgress<AgentSkillScanStep>? progress = null)
    {
        var normalizedUrl = ValidateRepositoryUrl(repositoryUrl);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "Ailo", "skill-install", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryRoot)!);

        try
        {
            progress?.Report(AgentSkillScanStep.CloningRepository);
            await CloneRepositoryAsync(normalizedUrl, temporaryRoot, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(AgentSkillScanStep.ScanningSkills);
            var candidates = EnumerateSkillFiles(temporaryRoot, cancellationToken)
                .Where(skillFile => !IsGitMetadataPath(Path.GetRelativePath(temporaryRoot, skillFile)))
                .Select(skillFile =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var directory = Path.GetDirectoryName(skillFile)!;
                    var frontmatter = ReadFrontmatter(skillFile);
                    return frontmatter is null
                        ? null
                        : new AgentSkillInstallCandidate(
                            CreateId(Path.Combine(temporaryRoot, Path.GetRelativePath(temporaryRoot, directory))),
                            frontmatter.Value.Name,
                            frontmatter.Value.Description,
                            Path.GetRelativePath(temporaryRoot, directory));
                })
                .Where(candidate => candidate is not null)
                .Cast<AgentSkillInstallCandidate>()
                .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
                .ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            return new AgentSkillRepositoryScan(normalizedUrl, temporaryRoot, candidates);
        }
        catch
        {
            TryDeleteDirectory(temporaryRoot);
            throw;
        }
    }

    /// <summary>Copies selected packages into the selected Agent Skill directory layout.</summary>
    public async Task<IReadOnlyList<AgentSkillInstallCandidate>> InstallAsync(
        AgentSkillRepositoryScan repository,
        IEnumerable<string> candidateIds,
        AgentSkillInstallType installType,
        string? customBaseDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(candidateIds);
        ArgumentNullException.ThrowIfNull(installType);

        var selectedIds = candidateIds.ToHashSet(StringComparer.Ordinal);
        var selectedSkills = repository.Skills
            .Where(skill => selectedIds.Contains(skill.Id))
            .ToArray();
        if (selectedSkills.Length == 0)
            throw new InvalidOperationException("Select at least one skill to install.");

        var supportedType = _installTypes.FirstOrDefault(type =>
            string.Equals(type.Name, installType.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(type.RelativeDirectory, installType.RelativeDirectory, StringComparison.Ordinal));
        if (supportedType is null)
            throw new InvalidOperationException("The selected install type is not supported.");

        var installDirectory = supportedType.GetInstallDirectory(customBaseDirectory);
        var destinations = selectedSkills
            .Select(skill => Path.Combine(installDirectory, skill.Name))
            .ToArray();
        if (destinations.Distinct(PathComparer).Count() != destinations.Length)
            throw new InvalidOperationException("The selected skills contain duplicate names.");
        if (destinations.Any(Directory.Exists) || destinations.Any(File.Exists))
            throw new IOException("One or more selected skill directories already exist at the destination.");

        Directory.CreateDirectory(installDirectory);
        var installedAt = DateTimeOffset.UtcNow;
        try
        {
            foreach (var (skill, destination) in selectedSkills.Zip(destinations))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceDirectory = GetRepositorySkillDirectory(repository, skill);
                CopyDirectory(sourceDirectory, destination, cancellationToken);
                WriteInstallMetadata(destination, new AgentSkillInstallMetadata(
                    repository.RepositoryUrl,
                    installedAt,
                    installedAt));
            }

            if (!string.IsNullOrWhiteSpace(customBaseDirectory) && _settings is not null)
            {
                var customDirectories = CustomSkillsDirectories
                    .Append(Path.GetFullPath(customBaseDirectory))
                    .Distinct(PathComparer)
                    .ToArray();
                await _settings.SaveAsync(
                    AppSettingsService.CustomAgentSkillsDirectoriesKey,
                    JsonSerializer.Serialize(customDirectories, AiloJsonSerializerContext.Default.StringArray),
                    cancellationToken).ConfigureAwait(false);
                CustomSkillsDirectories = customDirectories;
            }
        }
        catch
        {
            foreach (var destination in destinations)
                TryDeleteDirectory(destination);
            throw;
        }

        return selectedSkills;
    }

    /// <summary>Scans an installed skill's recorded repository and replaces only that skill.</summary>
    public async Task UpdateAsync(
        AgentSkillDefinition skill,
        CancellationToken cancellationToken = default,
        IProgress<AgentSkillScanStep>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(skill);
        var metadata = skill.InstallMetadata
            ?? throw new InvalidOperationException("This skill does not have installation metadata.");

        using var repository = await ScanRepositoryAsync(metadata.RepositoryUrl, cancellationToken, progress)
            .ConfigureAwait(false);
        var candidate = repository.Skills.FirstOrDefault(item =>
            string.Equals(item.Name, skill.Name, StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
            throw new AgentSkillUpdateUnavailableException(skill.Name, metadata.RepositoryUrl);

        var sourceDirectory = GetRepositorySkillDirectory(repository, candidate);
        if (!Directory.Exists(skill.DirectoryPath))
            throw new InvalidOperationException("The selected skill directory is no longer available.");

        var parentDirectory = Path.GetDirectoryName(skill.DirectoryPath)
            ?? throw new InvalidOperationException("The selected skill directory has no parent directory.");
        var stagingDirectory = Path.Combine(parentDirectory, $".{skill.Name}.update-{Guid.NewGuid():N}");
        var backupDirectory = Path.Combine(parentDirectory, $".{skill.Name}.backup-{Guid.NewGuid():N}");
        var previousMoved = false;
        var replacementMoved = false;
        try
        {
            CopyDirectory(sourceDirectory, stagingDirectory, cancellationToken);
            WriteInstallMetadata(stagingDirectory, metadata with { UpdatedAt = DateTimeOffset.UtcNow });
            cancellationToken.ThrowIfCancellationRequested();

            Directory.Move(skill.DirectoryPath, backupDirectory);
            previousMoved = true;
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stagingDirectory, skill.DirectoryPath);
            replacementMoved = true;
        }
        catch
        {
            if (previousMoved && !Directory.Exists(skill.DirectoryPath) && Directory.Exists(backupDirectory))
            {
                try
                {
                    Directory.Move(backupDirectory, skill.DirectoryPath);
                    previousMoved = false;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
            if (replacementMoved)
                TryDeleteDirectory(backupDirectory);
        }
    }

    private async Task<HashSet<string>> ReadDisabledDirectoriesAsync(CancellationToken cancellationToken)
    {
        await _availabilityGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadDisabledDirectoriesCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _availabilityGate.Release();
        }
    }

    private async Task<HashSet<string>> ReadDisabledDirectoriesCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_availabilityPath))
            return new HashSet<string>(PathComparer);

        try
        {
            var json = await File.ReadAllTextAsync(_availabilityPath, cancellationToken).ConfigureAwait(false);
            var directories = JsonSerializer.Deserialize(json, AiloJsonSerializerContext.Default.StringArray) ?? [];
            return directories
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Select(Path.GetFullPath)
                .ToHashSet(PathComparer);
        }
        catch (JsonException)
        {
            return new HashSet<string>(PathComparer);
        }
        catch (IOException)
        {
            return new HashSet<string>(PathComparer);
        }
    }

    private async Task WriteDisabledDirectoriesAsync(HashSet<string> disabledDirectories, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_availabilityPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_availabilityPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(disabledDirectories.Order(PathComparer).ToArray(), AiloJsonSerializerContext.Default.StringArray);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _availabilityPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static IReadOnlyList<AgentSkillSourceDirectory> CreateDefaultSources(AppPaths paths)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var sources = new List<AgentSkillSourceDirectory>
        {
            new("Ailo", paths.SkillsDirectory),
            new("Codex", Path.Combine(profile, ".codex", "skills")),
            new("Claude Code", Path.Combine(profile, ".claude", "skills")),
            new("GitHub Copilot", Path.Combine(profile, ".copilot", "skills")),
            new("Agents", Path.Combine(profile, ".agents", "skills")),
            new("Other agents", Path.Combine(profile, ".config", "agents", "skills")),
            new("Gemini CLI", Path.Combine(profile, ".gemini", "skills")),
            new("OpenCode", Path.Combine(profile, ".opencode", "skills"))
        };

        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfig))
            sources.Add(new AgentSkillSourceDirectory("Other agents", Path.Combine(xdgConfig, "agents", "skills")));
        if (!string.IsNullOrWhiteSpace(codexHome))
            sources.Add(new AgentSkillSourceDirectory("Codex", Path.Combine(codexHome, "skills")));
        return sources;
    }

    private static IReadOnlyList<AgentSkillInstallType> CreateDefaultInstallTypes(AppPaths paths)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var codexDirectory = string.IsNullOrWhiteSpace(codexHome)
            ? Path.Combine(profile, ".codex", "skills")
            : Path.Combine(codexHome, "skills");

        return
        [
            new("Ailo", Path.Combine(".ailo", "skills"), paths.SkillsDirectory),
            new("Codex", Path.Combine(".codex", "skills"), codexDirectory),
            new("Claude Code", Path.Combine(".claude", "skills"), Path.Combine(profile, ".claude", "skills")),
            new("GitHub Copilot", Path.Combine(".copilot", "skills"), Path.Combine(profile, ".copilot", "skills")),
            new("Agents", Path.Combine(".agents", "skills"), Path.Combine(profile, ".agents", "skills")),
            new("Other agents", Path.Combine(".config", "agents", "skills"), Path.Combine(profile, ".config", "agents", "skills")),
            new("Gemini CLI", Path.Combine(".gemini", "skills"), Path.Combine(profile, ".gemini", "skills")),
            new("OpenCode", Path.Combine(".opencode", "skills"), Path.Combine(profile, ".opencode", "skills"))
        ];
    }

    private static IReadOnlyList<AgentSkillInstallType> CreateInstallTypesFromSources(IReadOnlyList<AgentSkillSourceDirectory> sources) =>
        sources
            .Where(source => !string.Equals(source.Name, "Custom", StringComparison.OrdinalIgnoreCase))
            .GroupBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateInstallTypeForSource(group.First()))
            .ToArray();

    private static string GetInstallRelativeDirectory(string sourceName) => sourceName switch
    {
        "Ailo" => Path.Combine(".ailo", "skills"),
        "Codex" => Path.Combine(".codex", "skills"),
        "Claude Code" => Path.Combine(".claude", "skills"),
        "GitHub Copilot" => Path.Combine(".copilot", "skills"),
        "Agents" => Path.Combine(".agents", "skills"),
        "Other agents" => Path.Combine(".config", "agents", "skills"),
        "Gemini CLI" => Path.Combine(".gemini", "skills"),
        "OpenCode" => Path.Combine(".opencode", "skills"),
        _ => Path.Combine("skills", sourceName.ToLowerInvariant().Replace(' ', '-'))
    };

    private static AgentSkillInstallType CreateInstallTypeForSource(AgentSkillSourceDirectory source) =>
        new(source.Name, GetInstallRelativeDirectory(source.Name), source.Path);

    private static AgentSkillInstallMetadata? ReadInstallMetadata(string directory)
    {
        var metadataPath = Path.Combine(directory, InstallMetadataFileName);
        if (!File.Exists(metadataPath))
            return null;

        try
        {
            var json = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize(json, AiloJsonSerializerContext.Default.AgentSkillInstallMetadata);
            return string.IsNullOrWhiteSpace(metadata?.RepositoryUrl) ? null : metadata;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteInstallMetadata(string directory, AgentSkillInstallMetadata metadata)
    {
        var metadataPath = Path.Combine(directory, InstallMetadataFileName);
        var json = JsonSerializer.Serialize(metadata, AiloJsonSerializerContext.Default.AgentSkillInstallMetadata);
        File.WriteAllText(metadataPath, json);
    }

    private static string ValidateRepositoryUrl(string repositoryUrl)
    {
        var value = repositoryUrl?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A Git repository URL is required.", nameof(repositoryUrl));

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" or "ssh" or "git" &&
            !string.IsNullOrWhiteSpace(uri.Host))
            return value;

        if (value.StartsWith("git@", StringComparison.OrdinalIgnoreCase) && value.Contains(':'))
            return value;

        throw new ArgumentException("Enter a valid Git repository URL.", nameof(repositoryUrl));
    }

    private static async Task CloneRepositoryAsync(
        string repositoryUrl,
        string destination,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("clone");
        startInfo.ArgumentList.Add("--depth");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(repositoryUrl);
        startInfo.ArgumentList.Add(destination);

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start Git.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Git is not installed or is not available on PATH.", exception);
        }

        using (process)
        using (cancellationToken.Register(() => TryKill(process)))
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                TryKill(process);
                throw;
            }

            _ = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(error) ? "The Git command failed." : error.Trim();
                throw new InvalidOperationException($"Git clone failed: {detail.Replace(repositoryUrl, "[repository]", StringComparison.Ordinal)}");
            }
        }
    }

    private static string GetRepositorySkillDirectory(AgentSkillRepositoryScan repository, AgentSkillInstallCandidate candidate)
    {
        var directory = Path.GetFullPath(Path.Combine(repository.RepositoryPath, candidate.RelativeDirectory));
        var relativePath = Path.GetRelativePath(repository.RepositoryPath, directory);
        if (relativePath is "." or ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
            throw new InvalidOperationException("The repository contains an invalid skill directory.");
        if (!Directory.Exists(directory))
            throw new InvalidOperationException($"Skill '{candidate.Name}' is no longer available in the cloned repository.");
        return directory;
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(source, file);
            if (IsGitMetadataPath(relativePath))
                continue;
            if (string.Equals(Path.GetFileName(relativePath), InstallMetadataFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            var sourceInfo = new FileInfo(file);
            if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                continue;
            var destinationFile = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: false);
        }
    }

    private static bool IsGitMetadataPath(string relativePath) =>
        relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase));

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static IEnumerable<string> EnumerateSkillFiles(string root, CancellationToken cancellationToken = default)
    {
        var files = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "SKILL.md", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            }))
            {
                cancellationToken.ThrowIfCancellationRequested();
                files.Add(file);
            }
            return files;
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static (string Name, string Description)? ReadFrontmatter(string skillFile)
    {
        try
        {
            using var reader = new StreamReader(skillFile, detectEncodingFromByteOrderMarks: true);
            if (!string.Equals(reader.ReadLine()?.Trim(), "---", StringComparison.Ordinal))
                return null;

            string? name = null;
            string? description = null;
            for (var lineNumber = 0; lineNumber < 80; lineNumber++)
            {
                var line = reader.ReadLine();
                if (line is null || string.Equals(line.Trim(), "---", StringComparison.Ordinal))
                    break;
                var separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;
                var key = line[..separator].Trim();
                var value = Unquote(line[(separator + 1)..].Trim());
                if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase)) name = value;
                if (string.Equals(key, "description", StringComparison.OrdinalIgnoreCase)) description = value;
            }

            return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(description) && SkillNamePattern.IsMatch(name)
                ? (name, description)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool ContainsScript(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            }).Any(file => ScriptExtensions.Contains(Path.GetExtension(file)));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string CreateId(string directory)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Unquote(string value) => value.Length >= 2 &&
        ((value[0] == '\"' && value[^1] == '\"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;

    /// <summary>
    /// Equivalent to the framework's documented subprocess runner, kept here because this
    /// desktop build does not reference the optional sample runner package. Arguments are
    /// passed with <see cref="ProcessStartInfo.ArgumentList"/>, never through a shell string.
    /// </summary>
    private static async Task<object?> RunSkillScriptAsync(
        AgentFileSkill skill,
        AgentFileSkillScript script,
        JsonElement? arguments,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        var processStartInfo = CreateScriptProcessStartInfo(script.FullPath, skill.Path);
        foreach (var argument in ReadArguments(arguments))
            processStartInfo.ArgumentList.Add(argument);

        using var process = Process.Start(processStartInfo)
            ?? throw new InvalidOperationException($"Could not start skill script '{script.FullPath}'.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await standardOutput.ConfigureAwait(false);
        var error = await standardError.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Skill script '{script.FullPath}' exited with code {process.ExitCode}: {error.Trim()}");
        return output.Trim();
    }

    private static ProcessStartInfo CreateScriptProcessStartInfo(string scriptPath, string workingDirectory)
    {
        var extension = Path.GetExtension(scriptPath);
        var executable = extension.ToLowerInvariant() switch
        {
            ".py" => "python3",
            ".js" => "node",
            ".sh" when OperatingSystem.IsWindows() => "bash",
            ".sh" => "/bin/sh",
            ".ps1" when OperatingSystem.IsWindows() => "powershell",
            ".ps1" => "pwsh",
            ".csx" => "dotnet-script",
            ".cs" => "dotnet",
            _ => throw new NotSupportedException($"Skill script type '{extension}' is not supported by the local script runner.")
        };
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(scriptPath);
        return startInfo;
    }

    private static IEnumerable<string> ReadArguments(JsonElement? arguments)
    {
        if (arguments is null || arguments.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];
        if (arguments.Value.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Skill script arguments must be a JSON array of strings.", nameof(arguments));

        var values = new List<string>();
        foreach (var element in arguments.Value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } value)
                throw new ArgumentException("Skill script arguments must contain only strings.", nameof(arguments));
            values.Add(value);
        }

        return values;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
