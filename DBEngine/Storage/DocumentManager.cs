namespace DBEngine.Storage;

/// <summary>
/// Assembles and disassembles records that are stored in a container's Data pages,
/// transparently spilling records that don't fit a single page into chained Overflow pages.
/// </summary>
internal static class DocumentManager
{
    private const byte InlineMarker = 0x01;
    private const byte OverflowMarker = 0x02;

    /// <summary>Header bytes used by an overflow-pointer slot: marker + total length + first overflow page id.</summary>
    private const int OverflowPointerSize = 1 + 4 + 4;

    /// <summary>
    /// Inserts a new record into the container described by <paramref name="meta"/>.
    /// </summary>
    /// <returns>The id of the newly stored record.</returns>
    internal static RecordId Insert(MetaPage meta, IReadOnlyDictionary<string, FieldValue> record)
    {
        byte[] encoded = RecordCodec.Encode(meta, record);
        byte[] content = BuildSlotContent(encoded);

        var page = FindOrCreatePageWithSpace(meta, content.Length + 4);
        int slot = DataPageDirectory.InsertContent(page, content);
        if (slot < 0)
            throw new InvalidOperationException("Failed to insert record: page reported space but insertion failed.");

        using var writer = new PageWriter(page.Header.PageId);
        writer.Write(page);

        return new RecordId { PageId = page.Header.PageId, Slot = (ushort)slot };
    }

    /// <summary>
    /// Reads the record at <paramref name="id"/>, or null if it does not exist or has been deleted.
    /// </summary>
    internal static Dictionary<string, FieldValue>? Read(MetaPage meta, RecordId id)
    {
        var page = ReadPage(id.PageId);
        if (page is null || id.Slot >= DataPageDirectory.GetSlotCount(page))
            return null;

        var slotContent = DataPageDirectory.GetContent(page, id.Slot);
        if (slotContent.IsEmpty)
            return null;

        byte[] encoded = ResolveContent(slotContent);
        return RecordCodec.Decode(meta, encoded);
    }

    /// <summary>
    /// Replaces the record at <paramref name="id"/> with <paramref name="record"/>.
    /// </summary>
    /// <returns>
    /// The id of the record after the update. This is equal to <paramref name="id"/> when the
    /// new value fits in place; otherwise the record is moved and a new id is returned.
    /// </returns>
    internal static RecordId Update(MetaPage meta, RecordId id, IReadOnlyDictionary<string, FieldValue> record)
    {
        var page = ReadPage(id.PageId) ?? throw new ArgumentException($"Page {id.PageId} does not exist.", nameof(id));
        if (id.Slot >= DataPageDirectory.GetSlotCount(page))
            throw new ArgumentException($"Slot {id.Slot} does not exist on page {id.PageId}.", nameof(id));

        var oldContent = DataPageDirectory.GetContent(page, id.Slot);
        if (oldContent.IsEmpty)
            throw new ArgumentException("Record has been deleted.", nameof(id));

        FreeOverflowIfAny(oldContent);

        byte[] encoded = RecordCodec.Encode(meta, record);
        byte[] newContent = BuildSlotContent(encoded);

        if (DataPageDirectory.TryUpdateInPlace(page, id.Slot, newContent))
        {
            using var writer = new PageWriter(page.Header.PageId);
            writer.Write(page);
            return id;
        }

        // Doesn't fit in place: tombstone the old slot and insert as a new record.
        DataPageDirectory.DeleteContent(page, id.Slot);
        using (var writer = new PageWriter(page.Header.PageId))
            writer.Write(page);

        return Insert(meta, record);
    }

    /// <summary>
    /// Deletes the record at <paramref name="id"/>. Returns false if it does not exist.
    /// </summary>
    internal static bool Delete(MetaPage meta, RecordId id)
    {
        var page = ReadPage(id.PageId);
        if (page is null || id.Slot >= DataPageDirectory.GetSlotCount(page))
            return false;

        var content = DataPageDirectory.GetContent(page, id.Slot);
        if (content.IsEmpty)
            return false;

        FreeOverflowIfAny(content);
        DataPageDirectory.DeleteContent(page, id.Slot);

        using var writer = new PageWriter(page.Header.PageId);
        writer.Write(page);
        return true;
    }

    /// <summary>
    /// Enumerates every live record stored in the container described by <paramref name="meta"/>.
    /// </summary>
    internal static IEnumerable<(RecordId Id, Dictionary<string, FieldValue> Record)> Scan(MetaPage meta)
    {
        uint pageId = meta.FirstDataPageId;

        while (pageId != Constants.NoPage)
        {
            var page = ReadPage(pageId) ?? throw new InvalidDataException($"Data page {pageId} is missing.");
            ushort slotCount = DataPageDirectory.GetSlotCount(page);

            for (int i = 0; i < slotCount; i++)
            {
                var content = DataPageDirectory.GetContent(page, i);
                if (content.IsEmpty)
                    continue;

                byte[] encoded = ResolveContent(content);
                yield return (new RecordId { PageId = pageId, Slot = (ushort)i }, RecordCodec.Decode(meta, encoded));
            }

            pageId = page.Header.NextPageId;
        }
    }

    // ---- helpers -------------------------------------------------------

    private static byte[] BuildSlotContent(byte[] encoded)
    {
        if (encoded.Length + 1 <= DataPageDirectory.MaxInlineContentSize)
        {
            var content = new byte[encoded.Length + 1];
            content[0] = InlineMarker;
            encoded.CopyTo(content, 1);
            return content;
        }

        uint firstOverflowPageId = WriteOverflowChain(encoded);

        var pointer = new byte[OverflowPointerSize];
        pointer[0] = OverflowMarker;
        BitConverter.TryWriteBytes(pointer.AsSpan(1), encoded.Length);
        BitConverter.TryWriteBytes(pointer.AsSpan(5), firstOverflowPageId);
        return pointer;
    }

    private static byte[] ResolveContent(ReadOnlySpan<byte> slotContent)
    {
        return slotContent[0] switch
        {
            InlineMarker => slotContent[1..].ToArray(),
            OverflowMarker => ReadOverflowChain(
                totalLength: BitConverter.ToInt32(slotContent[1..]),
                firstPageId: BitConverter.ToUInt32(slotContent[5..])),
            _ => throw new InvalidDataException($"Unknown record content marker: {slotContent[0]}")
        };
    }

    private static void FreeOverflowIfAny(ReadOnlySpan<byte> slotContent)
    {
        if (slotContent[0] != OverflowMarker)
            return;

        uint pageId = BitConverter.ToUInt32(slotContent[5..]);
        while (pageId != Constants.NoPage)
        {
            var page = ReadPage(pageId) ?? throw new InvalidDataException($"Overflow page {pageId} is missing.");
            uint next = page.Header.NextPageId;
            PageAllocator.FreePage(pageId);
            pageId = next;
        }
    }

    private static uint WriteOverflowChain(byte[] data)
    {
        int pageCount = (data.Length + Constants.PageDataSize - 1) / Constants.PageDataSize;
        uint firstPageId = Constants.NoPage;
        Page? previous = null;

        for (int i = 0; i < pageCount; i++)
        {
            var page = (Page)PageAllocator.AllocatePage(PageType.Overflow);

            int srcOffset = i * Constants.PageDataSize;
            int chunkLength = Math.Min(Constants.PageDataSize, data.Length - srcOffset);
            data.AsSpan(srcOffset, chunkLength).CopyTo(page.Data.Span);

            if (i == 0)
                firstPageId = page.Header.PageId;

            if (previous is not null)
            {
                var prevHeader = previous.Header;
                prevHeader.NextPageId = page.Header.PageId;
                previous.Header = prevHeader;

                using var prevWriter = new PageWriter(previous.Header.PageId);
                prevWriter.Write(previous);
            }

            previous = page;
        }

        if (previous is not null)
        {
            using var writer = new PageWriter(previous.Header.PageId);
            writer.Write(previous);
        }

        return firstPageId;
    }

    private static byte[] ReadOverflowChain(int totalLength, uint firstPageId)
    {
        var buffer = new byte[totalLength];
        int written = 0;
        uint pageId = firstPageId;

        while (written < totalLength && pageId != Constants.NoPage)
        {
            var page = ReadPage(pageId) ?? throw new InvalidDataException($"Overflow page {pageId} is missing.");
            int chunkLength = Math.Min(Constants.PageDataSize, totalLength - written);
            page.Data.Span[..chunkLength].CopyTo(buffer.AsSpan(written));
            written += chunkLength;
            pageId = page.Header.NextPageId;
        }

        return buffer;
    }

    /// <summary>Finds a Data page in the container's chain with enough free space, allocating a new one if needed.</summary>
    private static Page FindOrCreatePageWithSpace(MetaPage meta, int requiredBytes) =>
        PageChainHelper.FindOrCreatePageWithSpace(
            meta,
            meta.FirstDataPageId,
            PageType.Data,
            requiredBytes,
            static (m, id) => m.FirstDataPageId = id);

    private static Page? ReadPage(uint pageId) => PageChainHelper.ReadPage(pageId);
}
