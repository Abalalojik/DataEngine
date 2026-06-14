namespace DBEngine.Storage;

/// <summary>
/// Represents a page that tracks the state of up to 16352 database pages.
/// Each byte in the data region represents the state of one page.
/// When full, the next AllocationMapPage is chained via NextPageId.
/// </summary>
internal class AllocationMapPage(uint pageId) : Page(pageId, PageType.AllocationMap)
{
    /// <summary>Maximum number of pages this map can track.</summary>
    internal const int Capacity = Constants.PageDataSize;

    /// <summary>
    /// Gets the state of a page at the given index within this map.
    /// </summary>
    /// <param name="index">Index of the page within this map (0 to Capacity-1).</param>
    /// <returns>The state byte, or null if the index is out of range.</returns>
    internal byte? GetState(int index)
    {
        if (index < 0 || index >= Capacity) return null;
        return Data.Span[index];
    }

    /// <summary>
    /// Sets the state of a page at the given index within this map.
    /// </summary>
    /// <param name="index">Index of the page within this map (0 to Capacity-1).</param>
    /// <param name="state">The state to set. Use Constants.PageState* values.</param>
    /// <returns>True if successful, false if the index is out of range.</returns>
    internal bool SetState(int index, byte state)
    {
        if (index < 0 || index >= Capacity) return false;
        Data.Span[index] = state;
        return true;
    }

    /// <summary>
    /// Finds the index of the first free page in this map.
    /// </summary>
    /// <returns>The index of the first free page, or -1 if none found.</returns>
    internal int FindFirstFree()
    {
        for (int i = 0; i < Capacity; i++)
        {
            if (Data.Span[i] == Constants.PageStateFree)
                return i;
        }
        return -1;
    }
}