using DBEngine.Temporal;

namespace DBEngine.Storage;

/// <summary>
/// A typed value for a single field, as stored in or read from a Data page.
/// Wraps a boxed CLR value alongside the <see cref="FieldType"/> that describes how it is encoded.
/// </summary>
internal readonly struct FieldValue
{
    /// <summary>The declared type of this value.</summary>
    internal FieldType Type { get; }

    /// <summary>
    /// The boxed value. Null represents a SQL-style NULL for this field, regardless of <see cref="Type"/>.
    /// When non-null, the runtime type matches the table below:
    /// String/Enum -> string, Int32 -> int, Int64 -> long, Float -> float, Double -> double,
    /// Boolean -> bool, DateTime -> DateTime, DateTimeEra -> DateTimeEra, Guid -> Guid,
    /// Bytes -> byte[], TableRef/CollectionRef -> RecordId.
    /// </summary>
    internal object? Value { get; }

    internal bool IsNull => Value is null;

    private FieldValue(FieldType type, object? value)
    {
        Type = type;
        Value = value;
    }

    /// <summary>Creates a NULL value for the given field type.</summary>
    internal static FieldValue Null(FieldType type) => new(type, null);

    internal static FieldValue Of(string value) => new(FieldType.String, value);
    internal static FieldValue Of(int value) => new(FieldType.Int32, value);
    internal static FieldValue Of(long value) => new(FieldType.Int64, value);
    internal static FieldValue Of(float value) => new(FieldType.Float, value);
    internal static FieldValue Of(double value) => new(FieldType.Double, value);
    internal static FieldValue Of(bool value) => new(FieldType.Boolean, value);
    internal static FieldValue Of(DateTime value) => new(FieldType.DateTime, value);
    internal static FieldValue Of(DateTimeEra value) => new(FieldType.DateTimeEra, value);
    internal static FieldValue Of(Guid value) => new(FieldType.Guid, value);
    internal static FieldValue Of(byte[] value) => new(FieldType.Bytes, value);
    internal static FieldValue OfEnum(string enumValueName) => new(FieldType.Enum, enumValueName);
    internal static FieldValue OfTableRef(RecordId id) => new(FieldType.TableRef, id);
    internal static FieldValue OfCollectionRef(RecordId id) => new(FieldType.CollectionRef, id);

    internal string AsString() => (string)Value!;
    internal int AsInt32() => (int)Value!;
    internal long AsInt64() => (long)Value!;
    internal float AsFloat() => (float)Value!;
    internal double AsDouble() => (double)Value!;
    internal bool AsBoolean() => (bool)Value!;
    internal DateTime AsDateTime() => (DateTime)Value!;
    internal DateTimeEra AsDateTimeEra() => (DateTimeEra)Value!;
    internal Guid AsGuid() => (Guid)Value!;
    internal byte[] AsBytes() => (byte[])Value!;
    internal RecordId AsRecordId() => (RecordId)Value!;

    public override string ToString() => IsNull ? "null" : Value!.ToString() ?? string.Empty;
}
