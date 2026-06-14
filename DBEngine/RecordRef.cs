using DBEngine.Storage;

namespace DBEngine;

/// <summary>
/// A lightweight, serializable reference to a row in a table or a document in a collection.
/// </summary>
public readonly record struct RecordRef
{
    /// <summary>ID of the page containing the first fragment of the record.</summary>
    public uint PageId { get; init; }

    /// <summary>Index of the record's slot within that page.</summary>
    public ushort Slot { get; init; }

    /// <summary>A sentinel value representing "no record".</summary>
    public static readonly RecordRef None = new() { PageId = Constants.NoPage, Slot = 0 };

    /// <summary>True if this reference does not point to any record.</summary>
    public bool IsNone => PageId == Constants.NoPage;

    public RecordRef(uint pageId, ushort slot)
    {
        PageId = pageId;
        Slot = slot;
    }

    internal RecordRef(RecordId id) : this(id.PageId, id.Slot) { }

    internal RecordId ToRecordId() => new() { PageId = PageId, Slot = Slot };

    public override string ToString() => $"{PageId}:{Slot}";
}
