using Ailo.Services;

namespace Ailo.Tests;

public sealed class LegacyDataMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Ailo.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void MigrateDefaultDataDirectory_CopiesLegacyFilesAndRenamesDatabase()
    {
        var legacyDirectory = Path.Combine(_root, "Chater");
        var destinationDirectory = Path.Combine(_root, "Ailo");
        Directory.CreateDirectory(Path.Combine(legacyDirectory, "logs"));
        File.WriteAllText(Path.Combine(legacyDirectory, "chater.db"), "database");
        File.WriteAllText(Path.Combine(legacyDirectory, "chater.db-wal"), "wal");
        File.WriteAllText(Path.Combine(legacyDirectory, "logs", "old.log"), "log");

        LegacyDataMigration.MigrateDefaultDataDirectory(destinationDirectory, [legacyDirectory]);

        Assert.Equal("database", File.ReadAllText(Path.Combine(destinationDirectory, "ailo.db")));
        Assert.Equal("wal", File.ReadAllText(Path.Combine(destinationDirectory, "ailo.db-wal")));
        Assert.Equal("log", File.ReadAllText(Path.Combine(destinationDirectory, "logs", "old.log")));
        Assert.True(File.Exists(Path.Combine(legacyDirectory, "chater.db")));
    }

    [Fact]
    public void MigrateLegacyConfiguration_CopiesPointerToTheNewConfigurationPath()
    {
        var currentConfigurationPath = Path.Combine(_root, "Ailo.data-directory");
        var legacyConfigurationPath = Path.Combine(_root, "Chater.data-directory");
        var configuredDirectory = Path.Combine(_root, "custom-data");
        Directory.CreateDirectory(configuredDirectory);
        File.WriteAllText(legacyConfigurationPath, configuredDirectory);
        var configuration = new DataDirectoryConfiguration(currentConfigurationPath, legacyConfigurationPath);

        Assert.Equal(Path.GetFullPath(configuredDirectory), configuration.MigrateLegacyConfiguration());
        Assert.Equal(Path.GetFullPath(configuredDirectory), File.ReadAllText(currentConfigurationPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
