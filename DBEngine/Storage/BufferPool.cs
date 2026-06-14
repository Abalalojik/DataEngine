using System.Collections.Concurrent;

namespace DBEngine.Storage;

/// <summary>
/// A simple in-memory cache of recently read/written <see cref="Page"/>s, keyed by page id.
///
/// This avoids re-reading a page from disk on every access when multiple operations touch
/// the same page (e.g. repeated allocation-map lookups, or a hot data page being scanned and
/// updated by concurrent operations). Entries are stored as independent copies so callers can
/// freely mutate the <see cref="Page"/> they receive without corrupting the cache; writers
/// must call <see cref="Put"/> to publish their changes back.
///
/// The pool is intentionally unbounded for now (the working set for this database's expected
/// sizes is small relative to available memory) — see Phase 1 notes in IMPLEMENTATION_PLAN.md
/// for a future eviction policy if that assumption stops holding.
/// </summary>
internal static class BufferPool
{
    private static readonly ConcurrentDictionary<uint, Page> _pages = new();

    /// <summary>Returns a private copy of the cached page, or null if not cached.</summary>
    internal static Page? TryGet(uint pageId) =>
        _pages.TryGetValue(pageId, out var page) ? page.Clone() : null;

    /// <summary>Stores a private copy of <paramref name="page"/> in the cache, replacing any previous entry.</summary>
    internal static void Put(Page page) => _pages[page.Header.PageId] = page.Clone();

    /// <summary>Removes a page from the cache (e.g. after it has been freed).</summary>
    internal static void Invalidate(uint pageId) => _pages.TryRemove(pageId, out _);

    /// <summary>Clears the entire cache. Intended for test isolation between Database instances.</summary>
    internal static void Reset() => _pages.Clear();
}
