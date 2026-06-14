namespace DBEngine.Storage;

/// <summary>
/// Reads a single page from the database file.
/// Each instance owns its stream and is intended to run on its own thread.
/// </summary>
internal class PageReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly uint _pageId;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _disposed;

    /// <summary>
    /// Opens the database file for reading at the position of the given page.
    /// </summary>
    /// <param name="pageId">ID of the page to read.</param>
    internal PageReader(uint pageId)
    {
        _pageId = pageId;
        _stream = Database.Current!.RentStream();

        _stream.Seek((long)pageId * Constants.PageSize, SeekOrigin.Begin);
    }

    /// <summary>
    /// Reads the page from the file and reconstructs it.
    /// </summary>
    /// <remarks>
    /// Acquires a shared (read) latch on this page number for the duration of the read, so
    /// concurrent writers cannot tear a page mid-read. Satisfied first from the in-memory
    /// <see cref="BufferPool"/> if present, avoiding a disk read entirely for hot pages.
    /// </remarks>
    /// <returns>The reconstructed page, or null if the read failed.</returns>
    internal Page? Read()
    {
        if (_disposed) return null;
        if (Environment.CurrentManagedThreadId != _ownerThreadId) return null;

        var latch = PageLatchManager.GetLatch(_pageId);
        latch.EnterReadLock();
        try
        {
            var cached = BufferPool.TryGet(_pageId);
            if (cached is not null) return cached;

            try
            {
                // Read header
                Span<byte> headerBytes = stackalloc byte[Constants.PageHeaderSize];
                int bytesRead = _stream.Read(headerBytes);
                if (bytesRead < Constants.PageHeaderSize) return null;

                int offset = 0;

                uint pageId = BitConverter.ToUInt32(headerBytes[offset..]);
                offset += 4;

                PageType type = (PageType)headerBytes[offset];
                offset += 1;

                byte flags = headerBytes[offset];
                offset += 1;

                ushort freeBytes = BitConverter.ToUInt16(headerBytes[offset..]);
                offset += 2;

                uint nextPageId = BitConverter.ToUInt32(headerBytes[offset..]);
                offset += 4;

                uint prevPageId = BitConverter.ToUInt32(headerBytes[offset..]);
                offset += 4;

                // 16 reserved bytes - skip
                var page = new Page(pageId, type);
                var header = page.Header;
                header.Flags      = flags;
                header.FreeBytes  = freeBytes;
                header.NextPageId = nextPageId;
                header.PrevPageId = prevPageId;
                page.Header = header;

                // Read data
                bytesRead = _stream.Read(page.Data.Span);
                if (bytesRead < Constants.PageDataSize) return null;

                BufferPool.Put(page);
                return page;
            }
            catch
            {
                return null;
            }
        }
        finally
        {
            latch.ExitReadLock();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        Database.Current?.ReturnStream(_stream);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
