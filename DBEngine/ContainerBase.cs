using DBEngine.Storage;

namespace DBEngine;

/// <summary>
/// Shared CRUD, lookup and history operations for <see cref="Table"/> and <see cref="Collection"/>.
/// </summary>
public abstract class ContainerBase
{
    /// <summary>The name of this container.</summary>
    public string Name { get; }

    /// <summary>The field schema of this container, in declaration order.</summary>
    public IReadOnlyList<Field> Schema { get; }

    private readonly MetaPage _meta;
    private readonly List<FieldMeta> _fieldMeta;
    private readonly Dictionary<string, FieldMeta> _fieldByName;

    internal ContainerBase(MetaPage meta)
    {
        _meta = meta;
        Name = meta.CollectionName;
        _fieldMeta = meta.Fields;
        _fieldByName = meta.Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);
        Schema = meta.Fields.Select(DataTypeMapper.ToField).ToList();
    }

    /// <summary>Inserts a new record and returns a reference to it.</summary>
    public RecordRef Insert(Document document)
    {
        var record = DocumentConverter.ToRecord(document, _fieldMeta);
        var id = StorageManager.Insert(Name, record);
        return new RecordRef(id);
    }

    /// <summary>Reads a record by reference, or null if it does not exist.</summary>
    public Document? Get(RecordRef reference)
    {
        var record = StorageManager.Read(Name, reference.ToRecordId());
        return record is null ? null : DocumentConverter.FromRecord(record, _fieldMeta);
    }

    /// <summary>
    /// Replaces the record at <paramref name="reference"/> with <paramref name="document"/>.
    /// Fields not present in <paramref name="document"/> are cleared to NULL. The previous value of
    /// every changed, versioned field is archived and can be retrieved via <see cref="GetHistory"/>.
    /// </summary>
    /// <returns>
    /// A reference to the updated record. This is usually equal to <paramref name="reference"/>,
    /// but may differ if the new value no longer fits at its original location.
    /// </returns>
    public RecordRef Update(RecordRef reference, Document document)
    {
        var record = DocumentConverter.ToRecord(document, _fieldMeta);
        var newId = StorageManager.Update(Name, reference.ToRecordId(), record);
        return new RecordRef(newId);
    }

    /// <summary>Deletes the record at <paramref name="reference"/>. Returns false if it did not exist.</summary>
    public bool Delete(RecordRef reference) => StorageManager.Delete(Name, reference.ToRecordId());

    /// <summary>Enumerates every live record in this container.</summary>
    public IEnumerable<(RecordRef Reference, Document Document)> All()
    {
        foreach (var (id, record) in StorageManager.Scan(Name))
            yield return (new RecordRef(id), DocumentConverter.FromRecord(record, _fieldMeta));
    }

    /// <summary>
    /// Returns the records whose <paramref name="fieldName"/> equals <paramref name="value"/>.
    /// The field must be declared with <c>isIndexed: true</c>.
    /// </summary>
    public IEnumerable<RecordRef> Find(string fieldName, object value)
    {
        var field = RequireField(fieldName);
        var key = DocumentConverter.ToFieldValue(value, field);
        // Re-read the meta: index roots are updated on disk as records are inserted, so the
        // copy captured when this container handle was created can be stale (IndexRoots=NoPage).
        var meta = StorageManager.GetContainer(Name) ?? _meta;

        foreach (var id in IndexManager.Find(meta, fieldName, key))
            yield return new RecordRef(id);
    }

    /// <summary>
    /// Returns the records whose <paramref name="fieldName"/> falls within [<paramref name="min"/>, <paramref name="max"/>].
    /// Either bound may be omitted by passing null. The field must be declared with <c>isIndexed: true</c>.
    /// </summary>
    public IEnumerable<RecordRef> Range(string fieldName, object? min, object? max)
    {
        var field = RequireField(fieldName);
        var minValue = min is null ? (FieldValue?)null : DocumentConverter.ToFieldValue(min, field);
        var maxValue = max is null ? (FieldValue?)null : DocumentConverter.ToFieldValue(max, field);
        var meta = StorageManager.GetContainer(Name) ?? _meta;

        foreach (var id in IndexManager.Range(meta, fieldName, minValue, maxValue))
            yield return new RecordRef(id);
    }

    /// <summary>Returns the archived values of a record's versioned fields, oldest first.</summary>
    public IEnumerable<HistoryRecord> GetHistory(RecordRef reference)
    {
        foreach (var entry in StorageManager.GetHistory(Name, reference.ToRecordId()))
        {
            yield return new HistoryRecord
            {
                RecordRef = new RecordRef(entry.RecordId),
                FieldName = entry.FieldName,
                TimestampUtc = entry.TimestampUtc,
                Value = DocumentConverter.FromFieldValue(entry.Value)
            };
        }
    }

    private FieldMeta RequireField(string fieldName)
    {
        if (_fieldByName.TryGetValue(fieldName, out var field))
            return field;
        throw new ArgumentException($"Container '{Name}' has no field named '{fieldName}'.", nameof(fieldName));
    }
}
