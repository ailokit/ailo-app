namespace Ailo.Services;

public sealed class AppPaths
{
    public const string ApplicationName = AppIdentity.ApplicationName;

    public AppPaths(string applicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        ApplicationDataDirectory = Path.GetFullPath(applicationDataDirectory);
        LogsDirectory = Path.Combine(ApplicationDataDirectory, "logs");
    }

    public string ApplicationDataDirectory { get; }

    public string DatabasePath => Path.Combine(ApplicationDataDirectory, "ailo.db");

    public string LogsDirectory { get; }

    /// <summary>Log location used by releases before logs became part of the user data directory.</summary>
    public string LegacyLogsDirectory => Path.Combine(AppContext.BaseDirectory, "logs");

    public string ExportsDirectory => Path.Combine(ApplicationDataDirectory, "exports");

    public string AttachmentsDirectory => Path.Combine(ApplicationDataDirectory, "attachments");

    public static AppPaths CreateDefault()
    {
        var configuration = DataDirectoryConfiguration.CreateDefault();
        var configuredDirectory = configuration.MigrateLegacyConfiguration();
        var defaultDirectory = GetDefaultDataDirectory();
        var dataDirectory = configuredDirectory ?? defaultDirectory;

        if (configuredDirectory is null)
        {
            LegacyDataMigration.MigrateDefaultDataDirectory(defaultDirectory);
        }

        var paths = new AppPaths(dataDirectory);
        LegacyDataMigration.MigrateDatabaseFiles(paths.ApplicationDataDirectory);
        return paths;
    }

    public static string GetDefaultDataDirectory() => Path.Combine(GetUserApplicationDataRoot(), ApplicationName);

    internal static string GetDataDirectoryConfigurationPath() =>
        Path.Combine(GetUserApplicationDataRoot(), $"{ApplicationName}.data-directory");

    internal static string GetLegacyDataDirectoryConfigurationPath(string applicationName) =>
        Path.Combine(GetUserApplicationDataRoot(), $"{applicationName}.data-directory");

    internal static string GetLegacyDefaultDataDirectory(string applicationName) =>
        Path.Combine(GetUserApplicationDataRoot(), applicationName);

    internal static string GetUserApplicationDataRoot() => OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ApplicationDataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
        Directory.CreateDirectory(AttachmentsDirectory);
    }
}
