namespace DBEngine.Storage;

/// <summary>
/// Shared logic for walking and extending a singly-linked chain of pages (Data or History)
/// rooted at one of a <see cref="MetaPage"/>'s "First...PageId" fields.
/// </summary>
internal static class PageChainHelper
{
    /// <summary>
    /// Finds the first page in the chain starting at <paramref name="firstPageId"/> that has at
    /// least <paramref name="requiredBytes"/> of free space, allocating and linking a new page
    /// of <paramref name="pageType"/> if none qualifies.
    /// </summary>
    /// <param name="meta">The container whose chain is being extended.</param>
    /// <param name="firstPageId">Current head of the chain (<see cref="Constants.NoPage"/> if empty).</param>
    /// <param name="pageType">Type to use for a newly allocated page.</param>
    /// <param name="requiredBytes">Minimum free space required.</param>
    /// <param name="setFirstPageId">Called with the new head page id if the chain was empty.</param>
    /// <returns>A page with at least <paramref name="requiredBytes"/> of free space.</returns>
    internal static Page FindOrCreatePageWithSpace(
        MetaPage meta,
        uint firstPageId,
        PageType pageType,
        int requiredBytes,
        Action<MetaPage, uint> setFirstPageId)
    {
        uint pageId = firstPageId;
        Page? last = null;

        while (pageId != Constants.NoPage)
        {
            var page = ReadPage(pageId) ?? throw new InvalidDataException($"Page {pageId} is missing.");
            if (DataPageDirectory.FreeSpace(page) >= requiredBytes)
                return page;

            last = page;
            pageId = page.Header.NextPageId;
        }

        var newPage = PageAllocator.AllocatePage(pageType);

        if (last is null)
        {
            setFirstPageId(meta, newPage.Header.PageId);
            MetaPageIO.Write(meta);
        }
        else
        {
            var lastHeader = last.Header;
            lastHeader.NextPageId = newPage.Header.PageId;
            last.Header = lastHeader;

            var newHeader = newPage.Header;
            newHeader.PrevPageId = last.Header.PageId;
            newPage.Header = newHeader;

            using var lastWriter = new PageWriter(last.Header.PageId);
            lastWriter.Write(last);
        }

        return newPage;
    }

    internal static Page? ReadPage(uint pageId)
    {
        using var reader = new PageReader(pageId);
        return reader.Read();
    }
}
