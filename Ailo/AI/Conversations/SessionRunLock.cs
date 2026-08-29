using System.Collections.Concurrent;

namespace Ailo.AI.Conversations;

/// <summary>
/// Serializes agent runs per conversation because a persisted agent session cannot be advanced concurrently.
/// </summary>
public sealed class SessionRunLock
{
    // Locks are retained for the process lifetime. Conversation IDs are bounded by the local database.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <summary>Waits for exclusive access to a conversation and returns a lease that releases it.</summary>
    public async Task<IDisposable> AcquireAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        var semaphore = _locks.GetOrAdd(conversationId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
