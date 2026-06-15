using DBEngine.Storage;
using DBEngine.Temporal;
using Xunit;

namespace DBEngine.Tests;

public class ValueSerializerTests
{
    [Theory]
    [InlineData(FieldType.Int32)]
    [InlineData(FieldType.Int64)]
    [InlineData(FieldType.Float)]
    [InlineData(FieldType.Double)]
    [InlineData(FieldType.Boolean)]
    [InlineData(FieldType.DateTime)]
    [InlineData(FieldType.Guid)]
    [InlineData(FieldType.String)]
    [InlineData(FieldType.Bytes)]
    [InlineData(FieldType.Enum)]
    [InlineData(FieldType.DateTimeEra)]
    [InlineData(FieldType.TableRef)]
    [InlineData(FieldType.CollectionRef)]
    public void RoundTrips_NonNullValue(FieldType type)
    {
        var value = SampleValue(type);

        int size = ValueSerializer.GetSize(value);
        var buffer = new byte[size];

        int written = ValueSerializer.Write(buffer, value);
        Assert.Equal(size, written);

        var (decoded, read) = ValueSerializer.Read(buffer, type);
        Assert.Equal(size, read);
        Assert.Equal(type, decoded.Type);
        Assert.False(decoded.IsNull);

        AssertValuesEqual(value, decoded);
    }

    [Theory]
    [InlineData(FieldType.Int32)]
    [InlineData(FieldType.String)]
    [InlineData(FieldType.Bytes)]
    [InlineData(FieldType.DateTimeEra)]
    [InlineData(FieldType.TableRef)]
    public void RoundTrips_NullValue(FieldType type)
    {
        var value = FieldValue.Null(type);

        int size = ValueSerializer.GetSize(value);
        Assert.Equal(1, size);

        var buffer = new byte[size];
        int written = ValueSerializer.Write(buffer, value);
        Assert.Equal(1, written);

        var (decoded, read) = ValueSerializer.Read(buffer, type);
        Assert.Equal(1, read);
        Assert.True(decoded.IsNull);
        Assert.Equal(type, decoded.Type);
    }

    [Fact]
    public void RecordId_RoundTrips_ThroughWriteAndReadFrom()
    {
        var id = new RecordId { PageId = 1234, Slot = 56 };

        Span<byte> buffer = stackalloc byte[RecordId.Size];
        id.WriteTo(buffer);

        var decoded = RecordId.ReadFrom(buffer);
        Assert.Equal(id, decoded);
    }

    private static FieldValue SampleValue(FieldType type) => type switch
    {
        FieldType.String => FieldValue.Of("hello, world"),
        FieldType.Int32 => FieldValue.Of(42),
        FieldType.Int64 => FieldValue.Of(123456789012345L),
        FieldType.Float => FieldValue.Of(3.14f),
        FieldType.Double => FieldValue.Of(2.718281828),
        FieldType.Boolean => FieldValue.Of(true),
        FieldType.DateTime => FieldValue.Of(new DateTime(2026, 6, 14, 12, 30, 0, DateTimeKind.Utc)),
        FieldType.DateTimeEra => FieldValue.Of(new DateTimeEra(2026, 6, 14, "CE")),
        FieldType.Guid => FieldValue.Of(Guid.Parse("11111111-2222-3333-4444-555555555555")),
        FieldType.Bytes => FieldValue.Of(new byte[] { 1, 2, 3, 4, 5 }),
        FieldType.Enum => FieldValue.OfEnum("Active"),
        FieldType.TableRef => FieldValue.OfTableRef(new RecordId { PageId = 7, Slot = 3 }),
        FieldType.CollectionRef => FieldValue.OfCollectionRef(new RecordId { PageId = 9, Slot = 1 }),
        _ => throw new NotSupportedException($"No sample value for {type}")
    };

    private static void AssertValuesEqual(FieldValue expected, FieldValue actual)
    {
        switch (expected.Type)
        {
            case FieldType.Bytes:
                Assert.Equal(expected.AsBytes(), actual.AsBytes());
                break;
            case FieldType.DateTimeEra:
                Assert.Equal(expected.AsDateTimeEra(), actual.AsDateTimeEra());
                break;
            case FieldType.TableRef:
            case FieldType.CollectionRef:
                Assert.Equal(expected.AsRecordId(), actual.AsRecordId());
                break;
            default:
                Assert.Equal(expected.Value, actual.Value);
                break;
        }
    }
}
