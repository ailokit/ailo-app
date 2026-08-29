using Ailo.Data;

namespace Ailo.Services;

/// <summary>
/// Changes the directory used on the next application start and optionally copies all current user data there.
/// </summary>
public sealed class DataDirectoryService(
    AppPaths currentPaths,
    SqliteDatabase database,
    DataDirectoryConfiguration configuration)
{
    public string CurrentDataDirectory => currentPaths.ApplicationDataDirectory;

    public async Task SetDataDirectoryAsync(string dataDirectory, bool migrateData, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var destinationDirectory = Path.GetFullPath(dataDirectory);
        if (string.Equals(destinationDirectory, currentPaths.ApplicationDataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            configuration.SaveDataDirectory(destinationDirectory);
            return;
        }

        if (migrateData)
        {
            EnsureSeparateDirectories(currentPaths.ApplicationDataDirectory, destinationDirectory);
            await CheckpointDatabaseAsync(cancellationToken).ConfigureAwait(false);
            CopyDirectory(currentPaths.ApplicationDataDirectory, destinationDirectory, cancellationToken);
            CopyLegacyLogs(destinationDirectory, cancellationToken);
        }

        configuration.SaveDataDirectory(destinationDirectory);
    }

    private async Task CheckpointDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(FULL);";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureSeparateDirectories(string sourceDirectory, string destinationDirectory)
    {
        var sourceWithSeparator = EnsureTrailingSeparator(sourceDirectory);
        var destinationWithSeparator = EnsureTrailingSeparator(destinationDirectory);
        if (destinationWithSeparator.StartsWith(sourceWithSeparator, StringComparison.OrdinalIgnoreCase) ||
            sourceWithSeparator.StartsWith(destinationWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The new data directory must not contain the current data directory, or be contained by it.");
        }
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : string.Concat(path, Path.DirectorySeparatorChar);

    private void CopyLegacyLogs(string destinationDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(currentPaths.LegacyLogsDirectory) ||
            string.Equals(currentPaths.LegacyLogsDirectory, currentPaths.LogsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CopyDirectory(currentPaths.LegacyLogsDirectory, Path.Combine(destinationDirectory, "logs"), cancellationToken, overwrite: false);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken, bool overwrite = true)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationFile = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            if (overwrite || !File.Exists(destinationFile))
            {
                File.Copy(file, destinationFile, overwrite: true);
            }
        }
    }
}
