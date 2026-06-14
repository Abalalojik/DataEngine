using DBEngine.Storage;

namespace DBEngine;

/// <summary>
/// Converts between the public <see cref="DataType"/> enum and the internal
/// <see cref="FieldType"/> enum used by the storage layer.
/// </summary>
internal static class DataTypeMapper
{
    // DataType and FieldType are parallel enums with identical members in the same order (0–12).
    // Direct casts are safe; the range guards catch any value added to one enum but not the other.
    internal static FieldType ToFieldType(DataType type)
    {
        if ((uint)(int)type > 12)
            throw new NotSupportedException($"Unsupported data type: {type}");
        return (FieldType)(byte)type;
    }

    internal static DataType ToDataType(FieldType type)
    {
        if ((byte)type > 12)
            throw new NotSupportedException($"Unsupported field type: {type}");
        return (DataType)(byte)type;
    }

    internal static FieldMeta ToFieldMeta(Field field) => new()
    {
        Name = field.Name,
        Type = ToFieldType(field.DataType),
        IsVersioned = field.IsVersioned,
        IsRequired = field.IsRequired,
        IsIndexed = field.IsIndexed
    };

    internal static Field ToField(FieldMeta meta) =>
        new(meta.Name, ToDataType(meta.Type), meta.IsVersioned, meta.IsRequired, meta.IsIndexed);
}
