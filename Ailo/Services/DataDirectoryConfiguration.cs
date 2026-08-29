namespace Ailo.Services;

/// <summary>
/// Stores only the bootstrap pointer to the user-selected data directory. The actual user data remains in
/// <see cref="AppPaths.ApplicationDataDirectory"/>, allowing the pointer to be read before SQLite is opened.
/// </summary>
public sealed class DataDirectoryConfiguration
{
    private readonly string _configurationPath;
    public DataDirectoryConfiguration(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        _configurationPath = Path.GetFullPath(configurationPath);
    }

    public static DataDirectoryConfiguration CreateDefault() =>
        new(AppPaths.GetDataDirectoryConfigurationPath());

    public string? GetDataDirectory()
    {
        return TryRead(_configurationPath);
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
