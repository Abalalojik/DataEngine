namespace DBEngine.Storage;

/// <summary>
/// Top-level orchestration: container (table/collection) management plus read/write/archival
/// of records, built on top of <see cref="PageAllocator"/>, <see cref="DocumentManager"/> and
/// <see cref="HistoryManager"/>.
/// </summary>
internal static class StorageManager
{
    /// <summary>
    /// Creates a new table or collection with the given schema and registers it in the
    /// database's meta page chain.
    /// </summary>
    internal static MetaPage CreateContainer(string name, ContainerType type, List<FieldMeta> fields)
    {
        if (GetContainer(name) is not null)
            throw new InvalidOperationException($"A container named '{name}' already exists.");

        var allocated = PageAllocator.AllocatePage(PageType.Meta);
        var meta = new MetaPage(allocated.Header.PageId)
        {
            CollectionName = name,
            ContainerType = type,
            Fields = fields
        };

        var root = PageAllocator.ReadRoot();

        var header = meta.Header;
        header.NextPageId = root.FirstMetaPageId;
        meta.Header = header;

        MetaPageIO.Write(meta);

        root.FirstMetaPageId = meta.Header.PageId;
        PageAllocator.WriteRoot(root);

        foreach (var field in fields)
        {
            if (field.IsIndexed)
                IndexManager.EnsureIndex(meta, field.Name);
        }

        return meta;
    }

    /// <summary>Returns the container with the given name, or null if it does not exist.</summary>
    internal static MetaPage? GetContainer(string name)
    {
        foreach (var meta in ListContainers())
        {
            if (meta.CollectionName == name)
                return meta;
        }

        return null;
    }

    /// <summary>Enumerates every table and collection defined in the database.</summary>
    internal static IEnumerable<MetaPage> ListContainers()
    {
        var root = PageAllocator.ReadRoot();
        uint pageId = root.FirstMetaPageId;

        while (pageId != Constants.NoPage)
        {
            var meta = MetaPageIO.Read(pageId);
            yield return meta;
            pageId = meta.Header.NextPageId;
        }
    }

    /// <summary>
    /// Permanently removes a container, freeing its meta page, data pages (including any
    /// overflow chains), index pages and history pages.
    /// </summary>
    internal static void DropContainer(string name)
    {
        var root = PageAllocator.ReadRoot();
        uint pageId = root.FirstMetaPageId;
        uint prevPageId = Constants.NoPage;

        while (pageId != Constants.NoPage)
        {
            var meta = MetaPageIO.Read(pageId);

            if (meta.CollectionName == name)
            {
                FreeDataChain(meta);
                foreach (var rootPageId in meta.IndexRoots.Values)
                    FreeSimpleChain(rootPageId);
                FreeSimpleChain(meta.FirstHistoryPageId);

                uint next = meta.Header.NextPageId;
                if (prevPageId == Constants.NoPage)
                {
                    root.FirstMetaPageId = next;
                    PageAllocator.WriteRoot(root);
                }
                else
                {
                    var prevMeta = MetaPageIO.Read(prevPageId);
                    var prevHeader = prevMeta.Header;
                    prevHeader.NextPageId = next;
                    prevMeta.Header = prevHeader;
                    MetaPageIO.Write(prevMeta);
                }

                PageAllocator.FreePage(pageId);
                return;
            }

            prevPageId = pageId;
            pageId = meta.Header.NextPageId;
        }

        throw new ArgumentException($"No container named '{name}' exists.", nameof(name));
    }

    /// <summary>Inserts a new record into the named container.</summary>
    internal static RecordId Insert(string containerName, IReadOnlyDictionary<string, FieldValue> record)
    {
        var meta = RequireContainer(containerName);
        ValidateRequired(meta, record);

        var id = DocumentManager.Insert(meta, record);

        foreach (var field in meta.Fields)
        {
            if (!field.IsIndexed) continue;
            var value = record.TryGetValue(field.Name, out var v) ? v : FieldValue.Null(field.Type);
            if (!value.IsNull)
                IndexManager.Insert(meta, field.Name, value, id);
        }

        return id;
    }

    /// <summary>Reads a record by id, or null if it does not exist.</summary>
    internal static Dictionary<string, FieldValue>? Read(string containerName, RecordId id)
    {
        var meta = RequireContainer(containerName);
        return DocumentManager.Read(meta, id);
    }

    /// <summary>
    /// Replaces a record, archiving the previous value of every field that changes.
    /// </summary>
    /// <returns>
    /// The id of the record after the update (unchanged unless the new value no longer fits
    /// in its current page, in which case the record is moved).
    /// </returns>
    internal static RecordId Update(string containerName, RecordId id, IReadOnlyDictionary<string, FieldValue> record)
    {
        var meta = RequireContainer(containerName);
        ValidateRequired(meta, record);

        var existing = DocumentManager.Read(meta, id)
            ?? throw new ArgumentException($"Record {id} does not exist in '{containerName}'.", nameof(id));

        var timestamp = DateTime.UtcNow;

        foreach (var field in meta.Fields)
        {
            var oldValue = existing.TryGetValue(field.Name, out var ov) ? ov : FieldValue.Null(field.Type);
            var newValue = record.TryGetValue(field.Name, out var nv) ? nv : FieldValue.Null(field.Type);

            if (!ValuesEqual(oldValue, newValue))
                HistoryManager.Append(meta, id, field.Name, oldValue, timestamp);
        }

        var newId = DocumentManager.Update(meta, id, record);

        foreach (var field in meta.Fields)
        {
            if (!field.IsIndexed) continue;

            var oldValue = existing.TryGetValue(field.Name, out var ov) ? ov : FieldValue.Null(field.Type);
            var newValue = record.TryGetValue(field.Name, out var nv) ? nv : FieldValue.Null(field.Type);

            if (ValuesEqual(oldValue, newValue) && newId == id)
                continue;

            if (!oldValue.IsNull)
                IndexManager.Delete(meta, field.Name, oldValue, id);
            if (!newValue.IsNull)
                IndexManager.Insert(meta, field.Name, newValue, newId);
        }

        return newId;
    }

    /// <summary>Deletes a record by id. Returns false if it did not exist.</summary>
    internal static bool Delete(string containerName, RecordId id)
    {
        var meta = RequireContainer(containerName);

        var existing = DocumentManager.Read(meta, id);
        if (existing is null)
            return false;

        foreach (var field in meta.Fields)
        {
            if (!field.IsIndexed) continue;

            var value = existing.TryGetValue(field.Name, out var v) ? v : FieldValue.Null(field.Type);
            if (!value.IsNull)
                IndexManager.Delete(meta, field.Name, value, id);
        }

        return DocumentManager.Delete(meta, id);
    }

    // ---- schema alteration -------------------------------------------------

    /// <summary>
    /// Appends a new field to a container's schema and re-encodes every existing record
    /// to include a NULL slot for the new field. If the field is indexed an empty index
    /// chain is registered.
    /// </summary>
    internal static void AddField(string containerName, FieldMeta field)
    {
        var meta = RequireContainer(containerName);

        if (meta.Fields.Any(f => f.Name == field.Name))
            throw new ArgumentException($"Field '{field.Name}' already exists in '{containerName}'.");

        // Snapshot all records under the old schema before changing it.
        var snapshot = DocumentManager.Scan(meta).ToList();

        meta.Fields.Add(field);
        MetaPageIO.Write(meta);

        if (field.IsIndexed)
            IndexManager.EnsureIndex(meta, field.Name);

        // Re-encode every record — the new field will be written as NULL.
        foreach (var (id, record) in snapshot)
        {
            if (!record.ContainsKey(field.Name))
                record[field.Name] = FieldValue.Null(field.Type);
            DocumentManager.Update(meta, id, record);
        }
    }

    /// <summary>
    /// Removes a field from a container's schema and re-encodes every existing record
    /// to drop that field's bytes. The field's index chain (if any) is freed.
    /// </summary>
    internal static void DropField(string containerName, string fieldName)
    {
        var meta = RequireContainer(containerName);

        var fieldMeta = meta.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (fieldMeta.Name is null)
            throw new ArgumentException($"Field '{fieldName}' does not exist in '{containerName}'.");

        // Snapshot all records under the old schema before changing it.
        var snapshot = DocumentManager.Scan(meta).ToList();

        // Drop the index chain for this field if it exists.
        if (meta.IndexRoots.TryGetValue(fieldName, out var indexRoot))
        {
            FreeSimpleChain(indexRoot);
            meta.IndexRoots.Remove(fieldName);
        }

        meta.Fields.Remove(fieldMeta);
        MetaPageIO.Write(meta);

        // Re-encode every record without the dropped field.
        foreach (var (id, record) in snapshot)
        {
            record.Remove(fieldName);
            DocumentManager.Update(meta, id, record);
        }
    }

    /// <summary>
    /// Renames a field in the schema. Because records are encoded positionally no record
    /// data is touched — only the meta page and any index root entry are updated.
    /// </summary>
    internal static void RenameField(string containerName, string oldName, string newName)
    {
        var meta = RequireContainer(containerName);

        int idx = meta.Fields.FindIndex(f => f.Name == oldName);
        if (idx < 0)
            throw new ArgumentException($"Field '{oldName}' does not exist in '{containerName}'.");

        if (meta.Fields.Any(f => f.Name == newName))
            throw new ArgumentException($"A field named '{newName}' already exists in '{containerName}'.");

        var old = meta.Fields[idx];
        meta.Fields[idx] = old with { Name = newName };

        if (meta.IndexRoots.Remove(oldName, out var root))
            meta.IndexRoots[newName] = root;

        MetaPageIO.Write(meta);
    }

    /// <summary>Enumerates every live record in the named container.</summary>
    internal static IEnumerable<(RecordId Id, Dictionary<string, FieldValue> Record)> Scan(string containerName)
    {
        var meta = RequireContainer(containerName);
        return DocumentManager.Scan(meta);
    }

    /// <summary>Returns the archived values of a record, oldest first.</summary>
    internal static IEnumerable<HistoryEntry> GetHistory(string containerName, RecordId id)
    {
        var meta = RequireContainer(containerName);
        return HistoryManager.GetHistory(meta, id);
    }

    // ---- helpers -------------------------------------------------------

    private static MetaPage RequireContainer(string name) =>
        GetContainer(name) ?? throw new ArgumentException($"No container named '{name}' exists.", nameof(name));

    private static void ValidateRequired(MetaPage meta, IReadOnlyDictionary<string, FieldValue> record)
    {
        foreach (var field in meta.Fields)
        {
            if (!field.IsRequired) continue;

            bool present = record.TryGetValue(field.Name, out var value) && !value.IsNull;
            if (!present)
                throw new ArgumentException($"Field '{field.Name}' is required.");
        }
    }

    private static bool ValuesEqual(FieldValue a, FieldValue b)
    {
        if (a.IsNull || b.IsNull)
            return a.IsNull && b.IsNull;

        if (a.Type == FieldType.Bytes)
            return a.AsBytes().AsSpan().SequenceEqual(b.AsBytes());

        return Equals(a.Value, b.Value);
    }

    private static void FreeDataChain(MetaPage meta)
    {
        // Deleting each record frees any overflow chains it owns.
        foreach (var (id, _) in DocumentManager.Scan(meta).ToList())
            DocumentManager.Delete(meta, id);

        FreeSimpleChain(meta.FirstDataPageId);
    }

    private static void FreeSimpleChain(uint firstPageId)
    {
        uint pageId = firstPageId;

        while (pageId != Constants.NoPage)
        {
            var page = PageChainHelper.ReadPage(pageId) ?? throw new InvalidDataException($"Page {pageId} is missing.");
            uint next = page.Header.NextPageId;
            PageAllocator.FreePage(pageId);
            pageId = next;
        }
    }
}
