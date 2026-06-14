namespace DBEngine.Storage;

/// <summary>
/// Manages the slot directory embedded in the data region of a Data, Index or History page.
///
/// Layout of <see cref="Page.Data"/>:
/// <code>
/// [0..2)   ushort SlotCount
/// [2..4)   ushort ContentStart  (0 means "uninitialized", i.e. PageDataSize)
/// [4..4+4*SlotCount) slot entries: ushort ContentOffset, ushort ContentLength (0 = tombstone)
/// [ContentStart..PageDataSize) packed content, growing downward from the end of the page
/// </code>
/// </summary>
internal static class DataPageDirectory
{
    private const int SlotCountOffset = 0;
    private const int ContentStartOffset = 2;
    private const int DirectoryStart = 4;
    private const int SlotEntrySize = 4;

    /// <summary>Largest content payload that can ever fit in a single page (with its own slot entry).</summary>
    internal const int MaxInlineContentSize = Constants.PageDataSize - DirectoryStart - SlotEntrySize;

    internal static ushort GetSlotCount(Page page) =>
        BitConverter.ToUInt16(page.Data.Span[SlotCountOffset..]);

    private static void SetSlotCount(Page page, ushort value) =>
        BitConverter.TryWriteBytes(page.Data.Span[SlotCountOffset..], value);

    internal static ushort GetContentStart(Page page)
    {
        ushort raw = BitConverter.ToUInt16(page.Data.Span[ContentStartOffset..]);
        return raw == 0 ? (ushort)Constants.PageDataSize : raw;
    }

    private static void SetContentStart(Page page, ushort value) =>
        BitConverter.TryWriteBytes(page.Data.Span[ContentStartOffset..], value);

    private static int SlotEntryOffset(int index) => DirectoryStart + index * SlotEntrySize;

    /// <summary>Gets the (offset, length) of the given slot. Length 0 means the slot is empty/deleted.</summary>
    internal static (ushort Offset, ushort Length) GetSlot(Page page, int index)
    {
        var span = page.Data.Span[SlotEntryOffset(index)..];
        return (BitConverter.ToUInt16(span), BitConverter.ToUInt16(span[2..]));
    }

    private static void SetSlot(Page page, int index, ushort offset, ushort length)
    {
        var span = page.Data.Span[SlotEntryOffset(index)..];
        BitConverter.TryWriteBytes(span, offset);
        BitConverter.TryWriteBytes(span[2..], length);
    }

    /// <summary>Bytes available for new content (and, if a new slot is needed, its directory entry).</summary>
    internal static int FreeSpace(Page page) =>
        GetContentStart(page) - (DirectoryStart + GetSlotCount(page) * SlotEntrySize);

    /// <summary>Returns the content bytes stored in the given slot, or an empty span for a tombstone.</summary>
    internal static ReadOnlySpan<byte> GetContent(Page page, int index)
    {
        var (offset, length) = GetSlot(page, index);
        return length == 0 ? ReadOnlySpan<byte>.Empty : page.Data.Span.Slice(offset, length);
    }

    /// <summary>
    /// Inserts <paramref name="content"/> into the page, reusing a tombstone slot index if one exists.
    /// </summary>
    /// <returns>The slot index used, or -1 if the page does not have enough free space.</returns>
    internal static int InsertContent(Page page, ReadOnlySpan<byte> content)
    {
        if (content.Length > MaxInlineContentSize)
            throw new ArgumentOutOfRangeException(nameof(content), "Content too large for a single page; use overflow pages.");

        ushort slotCount = GetSlotCount(page);

        int reuseIndex = -1;
        for (int i = 0; i < slotCount; i++)
        {
            if (GetSlot(page, i).Length == 0)
            {
                reuseIndex = i;
                break;
            }
        }

        int directoryGrowth = reuseIndex < 0 ? SlotEntrySize : 0;
        if (FreeSpace(page) < content.Length + directoryGrowth)
            return -1;

        ushort newOffset = (ushort)(GetContentStart(page) - content.Length);
        content.CopyTo(page.Data.Span.Slice(newOffset, content.Length));
        SetContentStart(page, newOffset);

        int index = reuseIndex >= 0 ? reuseIndex : slotCount;
        SetSlot(page, index, newOffset, (ushort)content.Length);

        if (reuseIndex < 0)
            SetSlotCount(page, (ushort)(slotCount + 1));

        UpdateFreeBytesHeader(page);
        return index;
    }

    /// <summary>
    /// Attempts to overwrite the content of an existing slot in place. Only possible if the
    /// new content is no larger than the space currently occupied by the slot.
    /// </summary>
    internal static bool TryUpdateInPlace(Page page, int index, ReadOnlySpan<byte> content)
    {
        var (offset, length) = GetSlot(page, index);
        if (length == 0 || content.Length > length)
            return false;

        content.CopyTo(page.Data.Span.Slice(offset, content.Length));
        SetSlot(page, index, offset, (ushort)content.Length);
        UpdateFreeBytesHeader(page);
        return true;
    }

    /// <summary>Marks the given slot as deleted (tombstone). The occupied bytes are not reclaimed.</summary>
    internal static void DeleteContent(Page page, int index)
    {
        SetSlot(page, index, 0, 0);
        UpdateFreeBytesHeader(page);
    }

    private static void UpdateFreeBytesHeader(Page page)
    {
        var header = page.Header;
        header.FreeBytes = (ushort)Math.Max(0, FreeSpace(page));
        page.Header = header;
    }
}
