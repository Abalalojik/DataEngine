using DBEngine.Storage;
using DBEngine.Temporal;

namespace DBEngine;

/// <summary>
/// Converts between the public <see cref="Document"/> bag-of-values type and the internal
/// <see cref="FieldValue"/> dictionaries used by the storage layer, guided by a container's schema.
/// </summary>
internal static class DocumentConverter
{
    /// <summary>Converts a public <see cref="Document"/> into internal field values, per <paramref name="schema"/>.</summary>
    internal static Dictionary<string, FieldValue> ToRecord(Document document, IReadOnlyList<FieldMeta> schema)
    {
        var record = new Dictionary<string, FieldValue>();

        foreach (var field in schema)
        {
            if (!document.Contains(field.Name))
                continue;

            record[field.Name] = ToFieldValue(document[field.Name], field);
        }

        return record;
    }

    /// <summary>Converts internal field values back into a public <see cref="Document"/>, per <paramref name="schema"/>.</summary>
    internal static Document FromRecord(IReadOnlyDictionary<string, FieldValue> record, IReadOnlyList<FieldMeta> schema)
    {
        var document = new Document();

        foreach (var field in schema)
        {
            if (record.TryGetValue(field.Name, out var value))
                document[field.Name] = FromFieldValue(value);
        }

        return document;
    }

    /// <summary>Converts a single CLR value into a <see cref="FieldValue"/>, per <paramref name="field"/>'s type.</summary>
    internal static FieldValue ToFieldValue(object? value, FieldMeta field)
    {
        if (value is null)
            return FieldValue.Null(field.Type);

        return field.Type switch
        {
            FieldType.String => FieldValue.Of(ConvertTo<string>(value, field.Name)),
            FieldType.Int32 => FieldValue.Of(ConvertToInt32(value, field.Name)),
            FieldType.Int64 => FieldValue.Of(ConvertToInt64(value, field.Name)),
            FieldType.Float => FieldValue.Of(ConvertToFloat(value, field.Name)),
            FieldType.Double => FieldValue.Of(ConvertToDouble(value, field.Name)),
            FieldType.Boolean => FieldValue.Of(ConvertTo<bool>(value, field.Name)),
            FieldType.DateTime => FieldValue.Of(ConvertTo<DateTime>(value, field.Name)),
            FieldType.DateTimeEra => FieldValue.Of(ConvertTo<DateTimeEra>(value, field.Name)),
            FieldType.Guid => FieldValue.Of(ConvertTo<Guid>(value, field.Name)),
            FieldType.Bytes => FieldValue.Of(ConvertTo<byte[]>(value, field.Name)),
            FieldType.Enum => FieldValue.OfEnum(ConvertTo<string>(value, field.Name)),
            FieldType.TableRef => FieldValue.OfTableRef(ConvertToRecordId(value, field.Name)),
            FieldType.CollectionRef => FieldValue.OfCollectionRef(ConvertToRecordId(value, field.Name)),
            _ => throw new NotSupportedException($"Unsupported field type: {field.Type}")
        };
    }

    /// <summary>Converts a <see cref="FieldValue"/> back into a plain CLR value (or null).</summary>
    internal static object? FromFieldValue(FieldValue value)
    {
        if (value.IsNull)
            return null;

        return value.Type switch
        {
            FieldType.String or FieldType.Enum => value.AsString(),
            FieldType.Int32 => value.AsInt32(),
            FieldType.Int64 => value.AsInt64(),
            FieldType.Float => value.AsFloat(),
            FieldType.Double => value.AsDouble(),
            FieldType.Boolean => value.AsBoolean(),
            FieldType.DateTime => value.AsDateTime(),
            FieldType.DateTimeEra => value.AsDateTimeEra(),
            FieldType.Guid => value.AsGuid(),
            FieldType.Bytes => value.AsBytes(),
            FieldType.TableRef or FieldType.CollectionRef => new RecordRef(value.AsRecordId()),
            _ => throw new NotSupportedException($"Unsupported field type: {value.Type}")
        };
    }

    private static T ConvertTo<T>(object value, string fieldName)
    {
        if (value is T typed) return typed;
        throw new InvalidCastException($"Field '{fieldName}' expects a value of type {typeof(T).Name}, but got {value.GetType().Name}.");
    }

    private static int ConvertToInt32(object value, string fieldName) => value switch
    {
        int i => i,
        short s => s,
        byte b => b,
        long l => checked((int)l),
        _ => throw new InvalidCastException($"Field '{fieldName}' expects an Int32-compatible value, but got {value.GetType().Name}.")
    };

    private static long ConvertToInt64(object value, string fieldName) => value switch
    {
        long l => l,
        int i => i,
        short s => s,
        byte b => b,
        _ => throw new InvalidCastException($"Field '{fieldName}' expects an Int64-compatible value, but got {value.GetType().Name}.")
    };

    private static float ConvertToFloat(object value, string fieldName) => value switch
    {
        float f => f,
        int i => i,
        double d => (float)d,
        _ => throw new InvalidCastException($"Field '{fieldName}' expects a Float-compatible value, but got {value.GetType().Name}.")
    };

    private static double ConvertToDouble(object value, string fieldName) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        _ => throw new InvalidCastException($"Field '{fieldName}' expects a Double-compatible value, but got {value.GetType().Name}.")
    };

    private static RecordId ConvertToRecordId(object value, string fieldName) => value switch
    {
        RecordRef r => r.ToRecordId(),
        RecordId id => id,
        _ => throw new InvalidCastException($"Field '{fieldName}' expects a RecordRef value, but got {value.GetType().Name}.")
    };
}
