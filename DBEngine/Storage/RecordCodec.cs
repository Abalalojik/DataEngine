namespace DBEngine.Storage;

/// <summary>
/// Encodes and decodes records (field name -> <see cref="FieldValue"/>) according to a
/// container's <see cref="MetaPage.Fields"/> schema. Fields are always written in schema order.
/// </summary>
internal static class RecordCodec
{
    /// <summary>Computes the encoded size, in bytes, of <paramref name="record"/> for the given schema.</summary>
    internal static int GetSize(MetaPage meta, IReadOnlyDictionary<string, FieldValue> record)
    {
        int size = 0;
        foreach (var field in meta.Fields)
            size += ValueSerializer.GetSize(GetOrNull(record, field));

        return size;
    }

    /// <summary>Encodes <paramref name="record"/> as a flat byte array, in schema field order.</summary>
    internal static byte[] Encode(MetaPage meta, IReadOnlyDictionary<string, FieldValue> record)
    {
        var buffer = new byte[GetSize(meta, record)];
        int offset = 0;

        foreach (var field in meta.Fields)
            offset += ValueSerializer.Write(buffer.AsSpan(offset), GetOrNull(record, field));

        return buffer;
    }

    /// <summary>Decodes a flat byte array produced by <see cref="Encode"/> back into a record.</summary>
    internal static Dictionary<string, FieldValue> Decode(MetaPage meta, ReadOnlySpan<byte> data)
    {
        var record = new Dictionary<string, FieldValue>(meta.Fields.Count);
        int offset = 0;

        foreach (var field in meta.Fields)
        {
            var (value, read) = ValueSerializer.Read(data[offset..], field.Type);
            record[field.Name] = value;
            offset += read;
        }

        return record;
    }

    private static FieldValue GetOrNull(IReadOnlyDictionary<string, FieldValue> record, FieldMeta field) =>
        record.TryGetValue(field.Name, out var value) ? value : FieldValue.Null(field.Type);
}
