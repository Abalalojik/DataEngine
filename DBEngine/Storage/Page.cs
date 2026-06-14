namespace DBEngine.Storage;

/// <summary>
/// Represents a single 16KB database page, consisting of a header and a data region.
/// </summary>
internal class Page(uint pageId, PageType type)
{
    /// <summary>The header of this page, containing metadata.</summary>
    internal PageHeader Header { get; set; } = new PageHeader
    {
        PageId     = pageId,
        Type       = type,
        Flags      = 0,
        FreeBytes  = Constants.PageDataSize,
        NextPageId = Constants.NoPage,
        PrevPageId = Constants.NoPage
    };

    /// <summary>The raw data region of this page.</summary>
    internal Memory<byte> Data { get; private set; } = new byte[Constants.PageDataSize];

    /// <summary>
    /// Creates an independent deep copy of this page (header + data), so the copy can be
    /// freely mutated or cached without affecting the original.
    /// </summary>
    internal Page Clone()
    {
        var clone = new Page(Header.PageId, Header.Type) { Header = Header };
        Data.CopyTo(clone.Data);
        return clone;
    }
}
