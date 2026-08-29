namespace Ailo.Services;

/// <summary>
/// Stores only the bootstrap pointer to the user-selected data directory. The actual user data remains in
/// <see cref="AppPaths.ApplicationDataDirectory"/>, allowing the pointer to be read before SQLite is opened.
/// </summary>
public sealed class DataDirectoryConfiguration
{
    private readonly string _configurationPath;
    private readonly IReadOnlyList<string> _legacyConfigurationPaths;

    public DataDirectoryConfiguration(string configurationPath, params string[] legacyConfigurationPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        _configurationPath = Path.GetFullPath(configurationPath);
        _legacyConfigurationPaths = legacyConfigurationPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static DataDirectoryConfiguration CreateDefault() =>
        new(
            AppPaths.GetDataDirectoryConfigurationPath(),
            AppPaths.GetLegacyDataDirectoryConfigurationPath("Chater-dev"),
            AppPaths.GetLegacyDataDirectoryConfigurationPath("Chater"));

    internal string? MigrateLegacyConfiguration()
    {
        var configuredDirectory = TryRead(_configurationPath);
        if (configuredDirectory is not null)
        {
            return configuredDirectory;
        }

        var legacyDirectory = _legacyConfigurationPaths
            .Select(TryRead)
            .FirstOrDefault(directory => directory is not null);
        if (legacyDirectory is not null)
        {
            SaveDataDirectory(legacyDirectory);
        }

        return legacyDirectory;
    }

    public string? GetDataDirectory()
    {
        return TryRead(_configurationPath) ?? _legacyConfigurationPaths
            .Select(TryRead)
            .FirstOrDefault(directory => directory is not null);
    }

    private static string? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var dataDirectory = File.ReadAllText(path).Trim();
            return string.IsNullOrWhiteSpace(dataDirectory) ? null : Path.GetFullPath(dataDirectory);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    public void SaveDataDirectory(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var directory = Path.GetDirectoryName(_configurationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_configurationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, Path.GetFullPath(dataDirectory));
            File.Move(temporaryPath, _configurationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
