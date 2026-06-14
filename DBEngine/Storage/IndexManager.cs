namespace DBEngine.Storage;

/// <summary>
/// Maintains secondary indexes for fields flagged <see cref="FieldMeta.IsIndexed"/>.
///
/// Each indexed field gets its own chain of Index pages (rooted at
/// <see cref="MetaPage.IndexRoots"/>[fieldName]), holding (key, <see cref="RecordId"/>) entries.
/// Entries are appended in insertion order; lookups perform a linear scan over the chain
/// comparing keys via <see cref="Compare"/>. This keeps the on-disk format simple while still
/// avoiding a full record decode for every candidate. Reorganizing the chain into a sorted
/// B-Tree with internal node pages for logarithmic lookups is tracked as future work.
/// </summary>
internal static class IndexManager
{
    /// <summary>Ensures an (initially empty) index chain is registered for <paramref name="fieldName"/>.</summary>
    internal static void EnsureIndex(MetaPage meta, string fieldName)
    {
        if (meta.IndexRoots.ContainsKey(fieldName))
            return;

        meta.IndexRoots[fieldName] = Constants.NoPage;
        MetaPageIO.Write(meta);
    }

    /// <summary>Adds an entry mapping <paramref name="key"/> to <paramref name="recordId"/>.</summary>
    internal static void Insert(MetaPage meta, string fieldName, FieldValue key, RecordId recordId)
    {
        byte[] content = Encode(key, recordId);

        uint rootId = meta.IndexRoots.TryGetValue(fieldName, out var r) ? r : Constants.NoPage;
        var page = PageChainHelper.FindOrCreatePageWithSpace(
            meta,
            rootId,
            PageType.Index,
            content.Length + 4,
            (m, id) => m.IndexRoots[fieldName] = id);

        DataPageDirectory.InsertContent(page, content);

        using var writer = new PageWriter(page.Header.PageId);
        writer.Write(page);
    }

    /// <summary>Removes the entry mapping <paramref name="key"/> to <paramref name="recordId"/>, if present.</summary>
    internal static void Delete(MetaPage meta, string fieldName, FieldValue key, RecordId recordId)
    {
        var type = GetField(meta, fieldName).Type;

        foreach (var (page, slot, entryKey, entryRecordId) in EnumerateEntries(meta, fieldName, type))
        {
            if (entryRecordId != recordId || Compare(entryKey, key) != 0)
                continue;

            DataPageDirectory.DeleteContent(page, slot);
            using var writer = new PageWriter(page.Header.PageId);
            writer.Write(page);
            return;
        }
    }

    /// <summary>Returns the ids of every record whose <paramref name="fieldName"/> equals <paramref name="key"/>.</summary>
    internal static IEnumerable<RecordId> Find(MetaPage meta, string fieldName, FieldValue key)
    {
        var type = GetField(meta, fieldName).Type;

        foreach (var (_, _, entryKey, entryRecordId) in EnumerateEntries(meta, fieldName, type))
        {
            if (Compare(entryKey, key) == 0)
                yield return entryRecordId;
        }
    }

    /// <summary>
    /// Returns the ids of every record whose <paramref name="fieldName"/> falls within
    /// [<paramref name="min"/>, <paramref name="max"/>] (either bound may be omitted).
    /// </summary>
    internal static IEnumerable<RecordId> Range(MetaPage meta, string fieldName, FieldValue? min, FieldValue? max)
    {
        var type = GetField(meta, fieldName).Type;

        foreach (var (_, _, entryKey, entryRecordId) in EnumerateEntries(meta, fieldName, type))
        {
            if (min is { } lo && Compare(entryKey, lo) < 0) continue;
            if (max is { } hi && Compare(entryKey, hi) > 0) continue;
            yield return entryRecordId;
        }
    }

    /// <summary>Drops and rebuilds the index for <paramref name="fieldName"/> by re-scanning all records.</summary>
    internal static void Rebuild(MetaPage meta, string fieldName)
    {
        if (meta.IndexRoots.TryGetValue(fieldName, out var rootId) && rootId != Constants.NoPage)
            FreeChain(rootId);

        meta.IndexRoots[fieldName] = Constants.NoPage;
        MetaPageIO.Write(meta);

        foreach (var (id, record) in DocumentManager.Scan(meta))
        {
            if (record.TryGetValue(fieldName, out var value) && !value.IsNull)
                Insert(meta, fieldName, value, id);
        }
    }

    /// <summary>
    /// Compares two values of the same <see cref="FieldType"/>. NULL sorts before any non-NULL value.
    /// </summary>
    internal static int Compare(FieldValue a, FieldValue b)
    {
        if (a.IsNull || b.IsNull)
            return (a.IsNull ? 0 : 1) - (b.IsNull ? 0 : 1);

        return a.Type switch
        {
            FieldType.String or FieldType.Enum => string.CompareOrdinal(a.AsString(), b.AsString()),
            FieldType.Int32 => a.AsInt32().CompareTo(b.AsInt32()),
            FieldType.Int64 => a.AsInt64().CompareTo(b.AsInt64()),
            FieldType.Float => a.AsFloat().CompareTo(b.AsFloat()),
            FieldType.Double => a.AsDouble().CompareTo(b.AsDouble()),
            FieldType.Boolean => a.AsBoolean().CompareTo(b.AsBoolean()),
            FieldType.DateTime => a.AsDateTime().CompareTo(b.AsDateTime()),
            FieldType.Guid => a.AsGuid().CompareTo(b.AsGuid()),
            FieldType.Bytes => CompareBytes(a.AsBytes(), b.AsBytes()),
            FieldType.TableRef or FieldType.CollectionRef => CompareRecordId(a.AsRecordId(), b.AsRecordId()),
            FieldType.DateTimeEra => CompareDateTimeEra(a.AsDateTimeEra(), b.AsDateTimeEra()),
            _ => throw new NotSupportedException($"Unsupported field type: {a.Type}")
        };
    }

    // ---- helpers -------------------------------------------------------

    private static byte[] Encode(FieldValue key, RecordId recordId)
    {
        int length = ValueSerializer.GetSize(key) + RecordId.Size;
        if (length > DataPageDirectory.MaxInlineContentSize)
            throw new NotSupportedException("Index key is too large to fit in a single page.");

        var content = new byte[length];
        int written = ValueSerializer.Write(content, key);
        recordId.WriteTo(content.AsSpan(written));
        return content;
    }

    private static IEnumerable<(Page Page, int Slot, FieldValue Key, RecordId RecordId)> EnumerateEntries(
        MetaPage meta, string fieldName, FieldType type)
    {
        if (!meta.IndexRoots.TryGetValue(fieldName, out var pageId) || pageId == Constants.NoPage)
            yield break;

        while (pageId != Constants.NoPage)
        {
            var page = PageChainHelper.ReadPage(pageId) ?? throw new InvalidDataException($"Index page {pageId} is missing.");
            ushort slotCount = DataPageDirectory.GetSlotCount(page);

            for (int i = 0; i < slotCount; i++)
            {
                var content = DataPageDirectory.GetContent(page, i);
                if (content.IsEmpty) continue;

                var (key, read) = ValueSerializer.Read(content, type);
                var recordId = RecordId.ReadFrom(content[read..]);
                yield return (page, i, key, recordId);
            }

            pageId = page.Header.NextPageId;
        }
    }

    private static void FreeChain(uint firstPageId)
    {
        uint pageId = firstPageId;

        while (pageId != Constants.NoPage)
        {
            var page = PageChainHelper.ReadPage(pageId) ?? throw new InvalidDataException($"Index page {pageId} is missing.");
            uint next = page.Header.NextPageId;
            PageAllocator.FreePage(pageId);
            pageId = next;
        }
    }

    private static FieldMeta GetField(MetaPage meta, string fieldName)
    {
        foreach (var field in meta.Fields)
        {
            if (field.Name == fieldName)
                return field;
        }

        throw new ArgumentException($"Unknown field '{fieldName}'.", nameof(fieldName));
    }

    private static int CompareBytes(byte[] a, byte[] b)
    {
        int min = Math.Min(a.Length, b.Length);
        for (int i = 0; i < min; i++)
        {
            int cmp = a[i].CompareTo(b[i]);
            if (cmp != 0) return cmp;
        }
        return a.Length.CompareTo(b.Length);
    }

    private static int CompareRecordId(RecordId a, RecordId b)
    {
        int cmp = a.PageId.CompareTo(b.PageId);
        return cmp != 0 ? cmp : a.Slot.CompareTo(b.Slot);
    }

    private static int CompareDateTimeEra(Temporal.DateTimeEra a, Temporal.DateTimeEra b)
    {
        int cmp = string.CompareOrdinal(a.Era ?? string.Empty, b.Era ?? string.Empty);
        if (cmp != 0) return cmp;
        cmp = a.Year.CompareTo(b.Year);
        if (cmp != 0) return cmp;
        cmp = a.Month.CompareTo(b.Month);
        return cmp != 0 ? cmp : a.Day.CompareTo(b.Day);
    }
}
