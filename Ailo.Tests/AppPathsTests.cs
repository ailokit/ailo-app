using Ailo.Services;

namespace Ailo.Tests;

public sealed class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Ailo.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnsureCreated_CreatesApplicationLogAndExportDirectories()
    {
        var paths = new AppPaths(_root);

        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.ApplicationDataDirectory));
        Assert.True(Directory.Exists(paths.LogsDirectory));
        Assert.True(Directory.Exists(paths.ExportsDirectory));
        Assert.Equal(Path.Combine(_root, "ailo.db"), paths.DatabasePath);
    }

    [Fact]
    public void CreateDefault_PutsLogsInTheUserDataDirectory()
    {
        var paths = AppPaths.CreateDefault();

        Assert.Equal(Path.Combine(paths.ApplicationDataDirectory, "logs"), paths.LogsDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
