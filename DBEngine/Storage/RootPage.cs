namespace DBEngine.Storage;

/// <summary>
/// Represents page 0 of the database file, containing global metadata.
/// </summary>
internal class RootPage() : Page(0, PageType.Header)
{
    /// <summary>Version of the file format. Used for compatibility checks.</summary>
    internal ushort FormatVersion { get; set; } = 1;

    /// <summary>Total number of pages in the database file.</summary>
    internal uint TotalPages { get; set; } = 1;

    /// <summary>ID of the first AllocationMap page.</summary>
    internal uint FirstAllocationMapPageId { get; set; } = Constants.NoPage;

    /// <summary>ID of the first History page.</summary>
    internal uint FirstHistoryPageId { get; set; } = Constants.NoPage;

    /// <summary>UTC ticks at the moment the database was created.</summary>
    internal long CreatedAt { get; set; } = DateTime.UtcNow.Ticks;

    /// <summary>CRC32 checksum of this page's data region, for integrity verification.</summary>
    internal uint Checksum { get; set; } = 0;

    /// <summary>ID of the first Meta page.</summary>
    internal uint FirstMetaPageId { get; set; } = Constants.NoPage;
}
