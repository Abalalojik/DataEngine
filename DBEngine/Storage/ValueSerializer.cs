using System.Text;
using DBEngine.Temporal;

namespace DBEngine.Storage;

/// <summary>
/// Converts <see cref="FieldValue"/> instances to and from their on-disk binary representation.
/// Every encoded value is prefixed with a single presence byte (0 = NULL, 1 = present) so that
/// optional fields can be stored without a separate bitmap.
/// </summary>
internal static class ValueSerializer
{
    private const byte NullFlag = 0;
    private const byte PresentFlag = 1;

    /// <summary>
    /// Computes the number of bytes required to serialize <paramref name="value"/>,
    /// including the leading presence byte.
    /// </summary>
    internal static int GetSize(FieldValue value)
    {
        if (value.IsNull) return 1;

        return 1 + value.Type switch
        {
            FieldType.String => 2 + Encoding.UTF8.GetByteCount(value.AsString()),
            FieldType.Int32 => 4,
            FieldType.Int64 => 8,
            FieldType.Float => 4,
            FieldType.Double => 8,
            FieldType.Boolean => 1,
            FieldType.DateTime => 8,
            FieldType.DateTimeEra => 12 + 2 + Encoding.UTF8.GetByteCount(value.AsDateTimeEra().Era ?? string.Empty),
            FieldType.Guid => 16,
            FieldType.Bytes => 2 + value.AsBytes().Length,
            FieldType.Enum => 2 + Encoding.UTF8.GetByteCount(value.AsString()),
            FieldType.TableRef => RecordId.Size,
            FieldType.CollectionRef => RecordId.Size,
            _ => throw new NotSupportedException($"Unsupported field type: {value.Type}")
        };
    }

    /// <summary>
    /// Writes <paramref name="value"/> to <paramref name="destination"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    internal static int Write(Span<byte> destination, FieldValue value)
    {
        if (value.IsNull)
        {
            destination[0] = NullFlag;
            return 1;
        }

        destination[0] = PresentFlag;
        var body = destination[1..];

        return 1 + value.Type switch
        {
            FieldType.String => WriteString(body, value.AsString()),
            FieldType.Int32 => WriteFixed(body, value.AsInt32()),
            FieldType.Int64 => WriteFixed(body, value.AsInt64()),
            FieldType.Float => WriteFixed(body, value.AsFloat()),
            FieldType.Double => WriteFixed(body, value.AsDouble()),
            FieldType.Boolean => WriteBoolean(body, value.AsBoolean()),
            FieldType.DateTime => WriteFixed(body, value.AsDateTime().ToUniversalTime().Ticks),
            FieldType.DateTimeEra => WriteDateTimeEra(body, value.AsDateTimeEra()),
            FieldType.Guid => WriteGuid(body, value.AsGuid()),
            FieldType.Bytes => WriteBytes(body, value.AsBytes()),
            FieldType.Enum => WriteString(body, value.AsString()),
            FieldType.TableRef => WriteRecordId(body, value.AsRecordId()),
            FieldType.CollectionRef => WriteRecordId(body, value.AsRecordId()),
            _ => throw new NotSupportedException($"Unsupported field type: {value.Type}")
        };
    }

    /// <summary>
    /// Reads a value of the given <paramref name="type"/> from <paramref name="source"/>.
    /// </summary>
    /// <returns>The decoded value and the number of bytes consumed.</returns>
    internal static (FieldValue Value, int BytesRead) Read(ReadOnlySpan<byte> source, FieldType type)
    {
        if (source[0] == NullFlag)
            return (FieldValue.Null(type), 1);

        var body = source[1..];

        switch (type)
        {
            case FieldType.String:
            {
                int consumed = ReadString(body, out var s);
                return (FieldValue.Of(s), 1 + consumed);
            }
            case FieldType.Int32:
                return (FieldValue.Of(BitConverter.ToInt32(body)), 1 + 4);
            case FieldType.Int64:
                return (FieldValue.Of(BitConverter.ToInt64(body)), 1 + 8);
            case FieldType.Float:
                return (FieldValue.Of(BitConverter.ToSingle(body)), 1 + 4);
            case FieldType.Double:
                return (FieldValue.Of(BitConverter.ToDouble(body)), 1 + 8);
            case FieldType.Boolean:
                return (FieldValue.Of(body[0] != 0), 1 + 1);
            case FieldType.DateTime:
                return (FieldValue.Of(new DateTime(BitConverter.ToInt64(body), DateTimeKind.Utc)), 1 + 8);
            case FieldType.DateTimeEra:
                return ReadDateTimeEra(body);
            case FieldType.Guid:
                return (FieldValue.Of(new Guid(body[..16])), 1 + 16);
            case FieldType.Bytes:
                return ReadBytes(body);
            case FieldType.Enum:
            {
                int consumed = ReadString(body, out var en);
                return (FieldValue.OfEnum(en), 1 + consumed);
            }
            case FieldType.TableRef:
                return (FieldValue.OfTableRef(RecordId.ReadFrom(body)), 1 + RecordId.Size);
            case FieldType.CollectionRef:
                return (FieldValue.OfCollectionRef(RecordId.ReadFrom(body)), 1 + RecordId.Size);
            default:
                throw new NotSupportedException($"Unsupported field type: {type}");
        }
    }

    private static int WriteFixed(Span<byte> dest, int value) { BitConverter.TryWriteBytes(dest, value); return 4; }
    private static int WriteFixed(Span<byte> dest, long value) { BitConverter.TryWriteBytes(dest, value); return 8; }
    private static int WriteFixed(Span<byte> dest, float value) { BitConverter.TryWriteBytes(dest, value); return 4; }
    private static int WriteFixed(Span<byte> dest, double value) { BitConverter.TryWriteBytes(dest, value); return 8; }
    private static int WriteBoolean(Span<byte> dest, bool value) { dest[0] = value ? (byte)1 : (byte)0; return 1; }
    private static int WriteGuid(Span<byte> dest, Guid value) { value.TryWriteBytes(dest); return 16; }
    private static int WriteRecordId(Span<byte> dest, RecordId value) { value.WriteTo(dest); return RecordId.Size; }

    private static int WriteString(Span<byte> dest, string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        BitConverter.TryWriteBytes(dest, (ushort)byteCount);
        Encoding.UTF8.GetBytes(value, dest[2..]);
        return 2 + byteCount;
    }

    private static int WriteBytes(Span<byte> dest, byte[] value)
    {
        BitConverter.TryWriteBytes(dest, (ushort)value.Length);
        value.CopyTo(dest[2..]);
        return 2 + value.Length;
    }

    private static int WriteDateTimeEra(Span<byte> dest, DateTimeEra value)
    {
        BitConverter.TryWriteBytes(dest, value.Year);
        BitConverter.TryWriteBytes(dest[4..], value.Month);
        BitConverter.TryWriteBytes(dest[8..], value.Day);
        return 12 + WriteString(dest[12..], value.Era ?? string.Empty);
    }

    private static int ReadString(ReadOnlySpan<byte> source, out string value)
    {
        ushort len = BitConverter.ToUInt16(source);
        value = Encoding.UTF8.GetString(source.Slice(2, len));
        return 2 + len;
    }

    private static int ReadBytes(ReadOnlySpan<byte> source, out byte[] value)
    {
        ushort len = BitConverter.ToUInt16(source);
        value = source.Slice(2, len).ToArray();
        return 2 + len;
    }

    private static (FieldValue, int) ReadBytes(ReadOnlySpan<byte> source)
    {
        int consumed = ReadBytes(source, out var value);
        return (FieldValue.Of(value), 1 + consumed);
    }

    private static (FieldValue, int) ReadDateTimeEra(ReadOnlySpan<byte> source)
    {
        int year = BitConverter.ToInt32(source);
        int month = BitConverter.ToInt32(source[4..]);
        int day = BitConverter.ToInt32(source[8..]);
        int consumed = 12 + ReadString(source[12..], out var era);
        var value = new DateTimeEra { Year = year, Month = month, Day = day, Era = era };
        return (FieldValue.Of(value), 1 + consumed);
    }
}
