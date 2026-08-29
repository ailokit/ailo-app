using Ailo.Data;
using Ailo.Services;

namespace Ailo.Tests;

public sealed class DataDirectoryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Ailo.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SetDataDirectoryAsync_WithMigration_CopiesAllDataAndOverwritesDestinationFiles()
    {
        var sourceDirectory = Path.Combine(_root, "source");
        var destinationDirectory = Path.Combine(_root, "destination");
        var paths = new AppPaths(sourceDirectory);
        paths.EnsureCreated();
        var database = new SqliteDatabase(paths.DatabasePath);
        await new DatabaseMigrator(database).MigrateAsync();
        await File.WriteAllTextAsync(Path.Combine(paths.LogsDirectory, "ailo-test.log"), "source log");
        await File.WriteAllTextAsync(Path.Combine(paths.AttachmentsDirectory, "image.png"), "source attachment");
        Directory.CreateDirectory(destinationDirectory);
        await File.WriteAllTextAsync(Path.Combine(destinationDirectory, "ailo.db"), "old database");
        var configuration = new DataDirectoryConfiguration(Path.Combine(_root, "bootstrap", "data-directory"));
        var service = new DataDirectoryService(paths, database, configuration);

        await service.SetDataDirectoryAsync(destinationDirectory, migrateData: true);

        Assert.Equal(Path.GetFullPath(destinationDirectory), configuration.GetDataDirectory());
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "ailo.db")));
        Assert.Equal("source log", await File.ReadAllTextAsync(Path.Combine(destinationDirectory, "logs", "ailo-test.log")));
        Assert.Equal("source attachment", await File.ReadAllTextAsync(Path.Combine(destinationDirectory, "attachments", "image.png")));
        Assert.True(File.Exists(paths.DatabasePath));
    }

    [Fact]
    public async Task SetDataDirectoryAsync_WithoutMigration_OnlyChangesTheStartupSelection()
    {
        var sourceDirectory = Path.Combine(_root, "source");
        var destinationDirectory = Path.Combine(_root, "destination");
        var paths = new AppPaths(sourceDirectory);
        paths.EnsureCreated();
        var configuration = new DataDirectoryConfiguration(Path.Combine(_root, "bootstrap", "data-directory"));
        var service = new DataDirectoryService(paths, new SqliteDatabase(paths.DatabasePath), configuration);

        await service.SetDataDirectoryAsync(destinationDirectory, migrateData: false);

        Assert.Equal(Path.GetFullPath(destinationDirectory), configuration.GetDataDirectory());
        Assert.False(Directory.Exists(destinationDirectory));
    }

    [Fact]
    public async Task SetDataDirectoryAsync_RejectsNestedMigrationTarget()
    {
        var sourceDirectory = Path.Combine(_root, "source");
        var paths = new AppPaths(sourceDirectory);
        paths.EnsureCreated();
        var configuration = new DataDirectoryConfiguration(Path.Combine(_root, "bootstrap", "data-directory"));
        var service = new DataDirectoryService(paths, new SqliteDatabase(paths.DatabasePath), configuration);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetDataDirectoryAsync(Path.Combine(sourceDirectory, "nested"), migrateData: true));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
