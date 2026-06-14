using System.Text;

namespace DBEngine.Storage;

/// <summary>
/// Appends and reads field-level history entries, stored as a simple append-only chain of
/// History pages rooted at <see cref="MetaPage.FirstHistoryPageId"/>.
///
/// Entry layout (within a <see cref="DataPageDirectory"/> slot):
/// <code>
/// RecordId (6 bytes) | TimestampUtc.Ticks (8 bytes) | FieldName (ushort len + UTF8) | Value (ValueSerializer)
/// </code>
/// </summary>
internal static class HistoryManager
{
    /// <summary>
    /// Archives <paramref name="oldValue"/> as the value of <paramref name="fieldName"/> on
    /// <paramref name="recordId"/> immediately before it was overwritten.
    /// </summary>
    internal static void Append(MetaPage meta, RecordId recordId, string fieldName, FieldValue oldValue, DateTime timestampUtc)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(fieldName);
        int valueSize = ValueSerializer.GetSize(oldValue);
        int contentLength = RecordId.Size + 8 + 2 + nameBytes.Length + valueSize;

        if (contentLength > DataPageDirectory.MaxInlineContentSize)
            throw new NotSupportedException("History entry is too large to fit in a single page.");

        var content = new byte[contentLength];
        int offset = 0;

        recordId.WriteTo(content.AsSpan(offset)); offset += RecordId.Size;
        BitConverter.TryWriteBytes(content.AsSpan(offset), timestampUtc.ToUniversalTime().Ticks); offset += 8;
        BitConverter.TryWriteBytes(content.AsSpan(offset), (ushort)nameBytes.Length); offset += 2;
        nameBytes.CopyTo(content.AsSpan(offset)); offset += nameBytes.Length;
        ValueSerializer.Write(content.AsSpan(offset), oldValue);

        var page = PageChainHelper.FindOrCreatePageWithSpace(
            meta,
            meta.FirstHistoryPageId,
            PageType.HistoryPage,
            content.Length + 4,
            static (m, id) => m.FirstHistoryPageId = id);

        DataPageDirectory.InsertContent(page, content);

        using var writer = new PageWriter(page.Header.PageId);
        writer.Write(page);
    }

    /// <summary>
    /// Enumerates all history entries for <paramref name="recordId"/>, across all fields,
    /// oldest first.
    /// </summary>
    internal static IEnumerable<HistoryEntry> GetHistory(MetaPage meta, RecordId recordId) =>
        EnumerateAll(meta).Where(e => e.RecordId == recordId).OrderBy(e => e.TimestampUtc);

    /// <summary>
    /// Enumerates the history of a single field of <paramref name="recordId"/>, oldest first.
    /// </summary>
    internal static IEnumerable<HistoryEntry> GetFieldHistory(MetaPage meta, RecordId recordId, string fieldName) =>
        GetHistory(meta, recordId).Where(e => e.FieldName == fieldName);

    private static IEnumerable<HistoryEntry> EnumerateAll(MetaPage meta)
    {
        uint pageId = meta.FirstHistoryPageId;

        while (pageId != Constants.NoPage)
        {
            var page = PageChainHelper.ReadPage(pageId) ?? throw new InvalidDataException($"History page {pageId} is missing.");
            ushort slotCount = DataPageDirectory.GetSlotCount(page);

            for (int i = 0; i < slotCount; i++)
            {
                var content = DataPageDirectory.GetContent(page, i);
                if (content.IsEmpty)
                    continue;

                yield return Decode(meta, content);
            }

            pageId = page.Header.NextPageId;
        }
    }

    private static HistoryEntry Decode(MetaPage meta, ReadOnlySpan<byte> content)
    {
        int offset = 0;

        var recordId = RecordId.ReadFrom(content[offset..]); offset += RecordId.Size;
        long ticks = BitConverter.ToInt64(content[offset..]); offset += 8;
        ushort nameLen = BitConverter.ToUInt16(content[offset..]); offset += 2;
        string fieldName = Encoding.UTF8.GetString(content.Slice(offset, nameLen)); offset += nameLen;

        var field = meta.Fields.FirstOrDefault(f => f.Name == fieldName);
        var (value, _) = ValueSerializer.Read(content[offset..], field.Type);

        return new HistoryEntry
        {
            RecordId = recordId,
            FieldName = fieldName,
            TimestampUtc = new DateTime(ticks, DateTimeKind.Utc),
            Value = value
        };
    }
}
