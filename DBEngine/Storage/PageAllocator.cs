namespace DBEngine.Storage;

/// <summary>
/// Allocates and frees pages within the currently open database, maintaining the
/// chain of <see cref="AllocationMapPage"/>s and the <see cref="RootPage"/> page count.
/// </summary>
internal static class PageAllocator
{
    /// <summary>
    /// Reads the root page (page 0) of the current database.
    /// </summary>
    internal static RootPage ReadRoot()
    {
        using var reader = new PageReader(0);
        var generic = reader.Read() ?? throw new InvalidDataException("Unable to read the root page.");

        var root = new RootPage
        {
            Header = generic.Header
        };

        var span = generic.Data.Span;
        int offset = 0;

        root.FormatVersion = BitConverter.ToUInt16(span[offset..]); offset += 2;
        root.TotalPages = BitConverter.ToUInt32(span[offset..]); offset += 4;
        root.FirstAllocationMapPageId = BitConverter.ToUInt32(span[offset..]); offset += 4;
        root.FirstHistoryPageId = BitConverter.ToUInt32(span[offset..]); offset += 4;
        root.CreatedAt = BitConverter.ToInt64(span[offset..]); offset += 8;
        root.Checksum = BitConverter.ToUInt32(span[offset..]); offset += 4;
        root.FirstMetaPageId = BitConverter.ToUInt32(span[offset..]); offset += 4;

        return root;
    }

    /// <summary>
    /// Persists the root page (page 0) of the current database.
    /// </summary>
    internal static void WriteRoot(RootPage root)
    {
        var span = root.Data.Span;
        int offset = 0;

        BitConverter.TryWriteBytes(span[offset..], root.FormatVersion); offset += 2;
        BitConverter.TryWriteBytes(span[offset..], root.TotalPages); offset += 4;
        BitConverter.TryWriteBytes(span[offset..], root.FirstAllocationMapPageId); offset += 4;
        BitConverter.TryWriteBytes(span[offset..], root.FirstHistoryPageId); offset += 4;
        BitConverter.TryWriteBytes(span[offset..], root.CreatedAt); offset += 8;
        BitConverter.TryWriteBytes(span[offset..], root.Checksum); offset += 4;
        BitConverter.TryWriteBytes(span[offset..], root.FirstMetaPageId); offset += 4;

        using var writer = new PageWriter(0);
        if (!writer.Write(root))
            throw new IOException("Unable to write the root page.");
    }

    /// <summary>
    /// Reads the <see cref="AllocationMapPage"/> with the given page id.
    /// </summary>
    internal static AllocationMapPage ReadAllocationMap(uint pageId)
    {
        using var reader = new PageReader(pageId);
        var generic = reader.Read() ?? throw new InvalidDataException($"Unable to read allocation map page {pageId}.");

        var map = new AllocationMapPage(pageId) { Header = generic.Header };
        generic.Data.CopyTo(map.Data);
        return map;
    }

    /// <summary>
    /// Persists an <see cref="AllocationMapPage"/>.
    /// </summary>
    internal static void WriteAllocationMap(AllocationMapPage map)
    {
        using var writer = new PageWriter(map.Header.PageId);
        if (!writer.Write(map))
            throw new IOException($"Unable to write allocation map page {map.Header.PageId}.");
    }

    /// <summary>
    /// Allocates a free page of the given <paramref name="type"/>, growing the allocation
    /// map chain and the database file as necessary.
    /// </summary>
    /// <returns>A freshly initialized, empty page of the requested type, ready to be populated and written.</returns>
    internal static Page AllocatePage(PageType type)
    {
        var root = ReadRoot();

        if (root.FirstAllocationMapPageId == Constants.NoPage)
        {
            // First allocation ever: place the first allocation map right after the root page.
            uint firstMapId = 1;
            var firstMap = new AllocationMapPage(firstMapId);
            WriteAllocationMap(firstMap);

            root.FirstAllocationMapPageId = firstMapId;
            root.TotalPages = Math.Max(root.TotalPages, firstMapId + 1);
            WriteRoot(root);
        }

        uint mapPageId = root.FirstAllocationMapPageId;

        while (true)
        {
            var map = ReadAllocationMap(mapPageId);
            int index = map.FindFirstFree();

            if (index >= 0)
            {
                uint allocatedPageId = mapPageId + 1 + (uint)index;

                map.SetState(index, Constants.PageStateOccupied);
                WriteAllocationMap(map);

                if (allocatedPageId + 1 > root.TotalPages)
                {
                    root.TotalPages = allocatedPageId + 1;
                    WriteRoot(root);
                }

                var page = new Page(allocatedPageId, type);
                using var writer = new PageWriter(allocatedPageId);
                writer.Write(page);

                return page;
            }

            if (map.Header.NextPageId != Constants.NoPage)
            {
                mapPageId = map.Header.NextPageId;
                continue;
            }

            // This allocation map is exhausted: chain a new one right after the
            // region it covers and retry.
            uint nextMapId = mapPageId + 1 + (uint)AllocationMapPage.Capacity;
            var nextMap = new AllocationMapPage(nextMapId);
            WriteAllocationMap(nextMap);

            map.Header = map.Header with { NextPageId = nextMapId };
            WriteAllocationMap(map);

            if (nextMapId + 1 > root.TotalPages)
            {
                root.TotalPages = nextMapId + 1;
                WriteRoot(root);
            }

            mapPageId = nextMapId;
        }
    }

    /// <summary>
    /// Marks the page with the given id as free, allowing it to be reused by future allocations.
    /// </summary>
    internal static void FreePage(uint pageId)
    {
        var root = ReadRoot();
        if (root.FirstAllocationMapPageId == Constants.NoPage)
            throw new InvalidOperationException("No allocation map exists yet.");

        uint mapPageId = root.FirstAllocationMapPageId;

        while (true)
        {
            var map = ReadAllocationMap(mapPageId);
            uint rangeStart = mapPageId + 1;
            uint rangeEnd = rangeStart + (uint)AllocationMapPage.Capacity; // exclusive

            if (pageId >= rangeStart && pageId < rangeEnd)
            {
                int index = (int)(pageId - rangeStart);
                map.SetState(index, Constants.PageStateFree);
                WriteAllocationMap(map);
                return;
            }

            if (map.Header.NextPageId == Constants.NoPage)
                throw new ArgumentOutOfRangeException(nameof(pageId), pageId, "Page id is not covered by any allocation map.");

            mapPageId = map.Header.NextPageId;
        }
    }
}
