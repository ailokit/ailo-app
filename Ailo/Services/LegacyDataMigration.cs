namespace Ailo.Services;

/// <summary>Moves data created by the previous product identity into Ailo's data layout.</summary>
internal static class LegacyDataMigration
{
    private const string LegacyDatabaseFileName = "chater.db";
    private const string CurrentDatabaseFileName = "ailo.db";

    public static void MigrateDefaultDataDirectory(string destinationDirectory, IEnumerable<string>? legacyDirectories = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        var sources = legacyDirectories ??
        [
            AppPaths.GetLegacyDefaultDataDirectory("Chater-dev"),
            AppPaths.GetLegacyDefaultDataDirectory("Chater")
        ];

        foreach (var sourceDirectory in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(sourceDirectory) ||
                string.Equals(Path.GetFullPath(sourceDirectory), Path.GetFullPath(destinationDirectory), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyMissingFiles(sourceDirectory, destinationDirectory);
        }

        MigrateManagedLogFiles(destinationDirectory);
        MigrateDatabaseFiles(destinationDirectory);
    }

    public static void MigrateDatabaseFiles(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Directory.Exists(dataDirectory))
        {
            return;
        }

        MoveIfNeeded(
            Path.Combine(dataDirectory, LegacyDatabaseFileName),
            Path.Combine(dataDirectory, CurrentDatabaseFileName));
        MoveIfNeeded(
            Path.Combine(dataDirectory, $"{LegacyDatabaseFileName}-wal"),
            Path.Combine(dataDirectory, $"{CurrentDatabaseFileName}-wal"));
        MoveIfNeeded(
            Path.Combine(dataDirectory, $"{LegacyDatabaseFileName}-shm"),
            Path.Combine(dataDirectory, $"{CurrentDatabaseFileName}-shm"));
    }

    private static void CopyMissingFiles(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var destinationFile = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            if (!File.Exists(destinationFile))
            {
                File.Copy(file, destinationFile);
            }
        }
    }

    private static void MoveIfNeeded(string sourcePath, string destinationPath)
    {
        if (File.Exists(sourcePath) && !File.Exists(destinationPath))
        {
            File.Move(sourcePath, destinationPath);
        }
    }

    private static void MigrateManagedLogFiles(string dataDirectory)
    {
        var logsDirectory = Path.Combine(dataDirectory, "logs");
        if (!Directory.Exists(logsDirectory))
        {
            return;
        }

        foreach (var sourcePath in Directory.EnumerateFiles(logsDirectory, "chater-*.log"))
        {
            var destinationPath = Path.Combine(logsDirectory, $"ailo-{Path.GetFileName(sourcePath)[7..]}");
            MoveIfNeeded(sourcePath, destinationPath);
        }
    }
}
