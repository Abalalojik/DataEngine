namespace DBEngine.Storage;

/// <summary>
/// Writes a single page to the database file.
/// Each instance owns its stream and is intended to run on its own thread.
/// </summary>
internal class PageWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _disposed;

    /// <summary>
    /// Opens the database file for writing at the position of the given page.
    /// </summary>
    /// <param name="pageId">ID of the page to write.</param>
    internal PageWriter(uint pageId)
    {
        _stream = Database.Current!.RentStream();

        _stream.Seek((long)pageId * Constants.PageSize, SeekOrigin.Begin);
    }

    /// <summary>
    /// Writes the given page (header + data) to the file.
    /// </summary>
    /// <param name="page">The page to write.</param>
    /// <returns>True if the write succeeded, false otherwise.</returns>
    internal bool Write(Page page)
    {
        if (_disposed) return false;
        if (Environment.CurrentManagedThreadId != _ownerThreadId) return false;

        var latch = PageLatchManager.GetLatch(page.Header.PageId);
        latch.EnterWriteLock();
        try
        {
            Span<byte> header = stackalloc byte[Constants.PageHeaderSize];
            int offset = 0;

            BitConverter.TryWriteBytes(header[offset..], page.Header.PageId);
            offset += 4;

            header[offset] = (byte)page.Header.Type;
            offset += 1;

            header[offset] = page.Header.Flags;
            offset += 1;

            BitConverter.TryWriteBytes(header[offset..], page.Header.FreeBytes);
            offset += 2;

            BitConverter.TryWriteBytes(header[offset..], page.Header.NextPageId);
            offset += 4;

            BitConverter.TryWriteBytes(header[offset..], page.Header.PrevPageId);
            offset += 4;

            // 16 reserved bytes — already zero via stackalloc
            _stream.Write(header);

            // Write data
            _stream.Write(page.Data.Span);
            _stream.Flush();

            BufferPool.Put(page);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            latch.ExitWriteLock();
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
