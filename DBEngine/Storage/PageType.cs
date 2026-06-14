namespace DBEngine.Storage;

/// <summary>
/// Defines the type of a database page.
/// </summary>
internal enum PageType : byte
{
    /// <summary>Page 0 of the file. Contains global database metadata.</summary>
    Header        = 0,
    /// <summary>Tracks which pages are free or occupied.</summary>
    AllocationMap = 1,
    /// <summary>Stores documents.</summary>
    Data          = 2,
    /// <summary>Stores B-Tree nodes for indexes.</summary>
    Index         = 3,
    /// <summary>Continuation of a document that exceeds a single page.</summary>
    Overflow      = 4,
    /// <summary>Stores previous versions of versioned fields.</summary>
    HistoryPage = 5,
    /// <summary>Stores collection schema and field metadata.</summary>
    Meta = 6
}
