using System.Collections.Concurrent;

namespace DBEngine.Storage;

/// <summary>
/// Provides one <see cref="ReaderWriterLockSlim"/> per page number, used by
/// <see cref="PageReader"/> and <see cref="PageWriter"/> to guard concurrent access to the
/// same page from multiple threads.
///
/// Latches are created lazily and kept for the lifetime of the process (the number of
/// distinct page ids touched is bounded by the database size, so this does not grow
/// unboundedly in practice). <see cref="LockRecursionPolicy.SupportsRecursion"/> is used so
/// that a single thread can safely re-enter a read or upgrade from a read it already holds
/// without deadlocking against itself.
/// </summary>
internal static class PageLatchManager
{
    private static readonly ConcurrentDictionary<uint, ReaderWriterLockSlim> _latches = new();

    /// <summary>Gets (or lazily creates) the latch guarding the given page.</summary>
    internal static ReaderWriterLockSlim GetLatch(uint pageId) =>
        _latches.GetOrAdd(pageId, static _ => new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion));

    /// <summary>
    /// Removes the cached latch for the given page id. Used when a page is freed so its
    /// (potentially heavyweight) lock object can be reclaimed; a fresh latch is created on
    /// next access if the page id is reused.
    /// </summary>
    internal static void Release(uint pageId) => _latches.TryRemove(pageId, out _);

    /// <summary>Clears all latches. Intended for test isolation between Database instances.</summary>
    internal static void Reset() => _latches.Clear();
}
