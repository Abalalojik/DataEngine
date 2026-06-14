namespace DBEngine.Storage;

/// <summary>
/// Identifies the physical location of a record (a row in a table or a document in a
/// collection): the page that holds its first fragment plus its slot index within that page.
/// </summary>
internal readonly record struct RecordId
{
    /// <summary>Fixed serialized size, in bytes, of a <see cref="RecordId"/> (4 + 2).</summary>
    internal const int Size = 6;

    /// <summary>ID of the page containing the first fragment of the record.</summary>
    internal uint PageId { get; init; }

    /// <summary>Index of the record's slot within that page.</summary>
    internal ushort Slot { get; init; }

    /// <summary>A sentinel value representing "no record".</summary>
    internal static readonly RecordId None = new() { PageId = Constants.NoPage, Slot = 0 };

    internal bool IsNone => PageId == Constants.NoPage;

    /// <summary>Writes this record id to <paramref name="destination"/> (6 bytes, little-endian).</summary>
    internal void WriteTo(Span<byte> destination)
    {
        BitConverter.TryWriteBytes(destination, PageId);
        BitConverter.TryWriteBytes(destination[4..], Slot);
    }

    /// <summary>Reads a <see cref="RecordId"/> from <paramref name="source"/> (6 bytes, little-endian).</summary>
    internal static RecordId ReadFrom(ReadOnlySpan<byte> source) => new()
    {
        PageId = BitConverter.ToUInt32(source),
        Slot = BitConverter.ToUInt16(source[4..])
    };

    public override string ToString() => $"{PageId}:{Slot}";
}
