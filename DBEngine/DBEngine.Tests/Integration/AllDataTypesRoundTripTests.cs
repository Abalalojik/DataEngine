using DBEngine.Temporal;
using Xunit;

namespace DBEngine.Tests.Integration;

/// <summary>
/// Comprehensive round-trip coverage for every <see cref="DataType"/> the storage layer supports,
/// including NULL handling (the leading presence byte in <c>ValueSerializer</c>).
///
/// This is the "store and retrieve any form of data possible" check requested for Phase 0 /
/// Feature 6 of the E2E suite: one table with a field of every DataType, insert a row with
/// real values, insert a row with all-NULL optional values, and verify Get() returns exactly
/// what was written for each type.
/// </summary>
public class AllDataTypesRoundTripTests : IDisposable
{
    private readonly string _path;

    public AllDataTypesRoundTripTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"dbengine-alltypes-{Guid.NewGuid():N}.dbeg");
    }

    public void Dispose()
    {
        Database.Current?.Dispose();
        if (File.Exists(_path))
            File.Delete(_path);
    }

    private Database OpenDatabase() => Database.Open(_path, replaceIfExists: true);

    /// <summary>Builds the "AllTypes" table covering all 13 DataType values (none required, so NULL is legal).</summary>
    private static Table CreateAllTypesTable(Database db) => db.CreateTable("AllTypes", new[]
    {
        new Field("StringField", DataType.String),
        new Field("Int32Field", DataType.Int32),
        new Field("Int64Field", DataType.Int64),
        new Field("FloatField", DataType.Float),
        new Field("DoubleField", DataType.Double),
        new Field("BooleanField", DataType.Boolean),
        new Field("DateTimeField", DataType.DateTime),
        new Field("DateTimeEraField", DataType.DateTimeEra),
        new Field("GuidField", DataType.Guid),
        new Field("BytesField", DataType.Bytes),
        new Field("EnumField", DataType.Enum),
        new Field("TableRefField", DataType.TableRef),
        new Field("CollectionRefField", DataType.CollectionRef),
    });

    [Fact]
    public void AllDataTypes_InsertAndGet_RoundTripsEveryType()
    {
        using var db = OpenDatabase();

        // Targets for TableRef / CollectionRef so we have real, resolvable RecordRefs.
        var refTargets = db.CreateTable("RefTargets", new[] { new Field("Label", DataType.String) });
        var tableRefTarget = refTargets.Insert(new Document { ["Label"] = "table-ref-target" });
        var collectionRefTarget = refTargets.Insert(new Document { ["Label"] = "collection-ref-target" });
        Assert.False(tableRefTarget.IsNone);
        Assert.False(collectionRefTarget.IsNone);

        var table = CreateAllTypesTable(db);

        var now = new DateTime(2026, 6, 14, 12, 34, 56, DateTimeKind.Utc);
        var era = new DateTimeEra(2026, 6, 14, "CE");
        var guid = Guid.NewGuid();
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0x10, 0x80 };

        var doc = new Document
        {
            ["StringField"] = "Hello, DBEngine!",
            ["Int32Field"] = -123456,
            ["Int64Field"] = 9_876_543_210L,
            ["FloatField"] = 3.14159f,
            ["DoubleField"] = 2.718281828459045,
            ["BooleanField"] = true,
            ["DateTimeField"] = now,
            ["DateTimeEraField"] = era,
            ["GuidField"] = guid,
            ["BytesField"] = bytes,
            ["EnumField"] = "SomeEnumValue",
            ["TableRefField"] = tableRefTarget,
            ["CollectionRefField"] = collectionRefTarget,
        };

        var reference = table.Insert(doc);
        Assert.False(reference.IsNone);

        var read = table.Get(reference);
        Assert.NotNull(read);

        Assert.Equal("Hello, DBEngine!", read!["StringField"]);
        Assert.Equal(-123456, read["Int32Field"]);
        Assert.Equal(9_876_543_210L, read["Int64Field"]);
        Assert.Equal(3.14159f, (float)read["FloatField"]!, precision: 5);
        Assert.Equal(2.718281828459045, (double)read["DoubleField"]!, precision: 10);
        Assert.Equal(true, read["BooleanField"]);
        Assert.Equal(now, read["DateTimeField"]);

        var readEra = Assert.IsType<DateTimeEra>(read["DateTimeEraField"]);
        Assert.Equal(era.Year, readEra.Year);
        Assert.Equal(era.Month, readEra.Month);
        Assert.Equal(era.Day, readEra.Day);
        Assert.Equal(era.Era, readEra.Era);

        Assert.Equal(guid, read["GuidField"]);
        Assert.Equal(bytes, read["BytesField"]);
        Assert.Equal("SomeEnumValue", read["EnumField"]);

        var readTableRef = Assert.IsType<RecordRef>(read["TableRefField"]);
        Assert.Equal(tableRefTarget, readTableRef);

        var readCollectionRef = Assert.IsType<RecordRef>(read["CollectionRefField"]);
        Assert.Equal(collectionRefTarget, readCollectionRef);

        // Resolve the refs back to confirm they point at real, readable records.
        var resolvedTableRef = refTargets.Get(readTableRef);
        Assert.NotNull(resolvedTableRef);
        Assert.Equal("table-ref-target", resolvedTableRef!["Label"]);

        var resolvedCollectionRef = refTargets.Get(readCollectionRef);
        Assert.NotNull(resolvedCollectionRef);
        Assert.Equal("collection-ref-target", resolvedCollectionRef!["Label"]);
    }

    [Fact]
    public void AllDataTypes_AllFieldsNull_RoundTripsAsNull()
    {
        using var db = OpenDatabase();
        var table = CreateAllTypesTable(db);

        // None of the fields are marked IsRequired, so an empty document means every
        // field is unset/NULL — exercises the presence-byte=0 path for all 13 types.
        var reference = table.Insert(new Document());
        Assert.False(reference.IsNone);

        var read = table.Get(reference);
        Assert.NotNull(read);

        Assert.Null(read!["StringField"]);
        Assert.Null(read["Int32Field"]);
        Assert.Null(read["Int64Field"]);
        Assert.Null(read["FloatField"]);
        Assert.Null(read["DoubleField"]);
        Assert.Null(read["BooleanField"]);
        Assert.Null(read["DateTimeField"]);
        Assert.Null(read["DateTimeEraField"]);
        Assert.Null(read["GuidField"]);
        Assert.Null(read["BytesField"]);
        Assert.Null(read["EnumField"]);
        Assert.Null(read["TableRefField"]);
        Assert.Null(read["CollectionRefField"]);
    }

    [Fact]
    public void AllDataTypes_EmptyStringAndZeroLengthBytes_RoundTrip()
    {
        using var db = OpenDatabase();
        var table = CreateAllTypesTable(db);

        var doc = new Document
        {
            ["StringField"] = string.Empty,
            ["BytesField"] = Array.Empty<byte>(),
            ["EnumField"] = string.Empty,
        };

        var reference = table.Insert(doc);
        var read = table.Get(reference);

        Assert.NotNull(read);
        Assert.Equal(string.Empty, read!["StringField"]);
        Assert.Equal(Array.Empty<byte>(), read["BytesField"]);
        Assert.Equal(string.Empty, read["EnumField"]);
    }

    [Fact]
    public void AllDataTypes_BoundaryNumericValues_RoundTrip()
    {
        using var db = OpenDatabase();
        var table = CreateAllTypesTable(db);

        var doc = new Document
        {
            ["Int32Field"] = int.MinValue,
            ["Int64Field"] = long.MaxValue,
            ["FloatField"] = float.MinValue,
            ["DoubleField"] = double.MaxValue,
            ["BooleanField"] = false,
        };

        var reference = table.Insert(doc);
        var read = table.Get(reference);

        Assert.NotNull(read);
        Assert.Equal(int.MinValue, read!["Int32Field"]);
        Assert.Equal(long.MaxValue, read["Int64Field"]);
        Assert.Equal(float.MinValue, read["FloatField"]);
        Assert.Equal(double.MaxValue, read["DoubleField"]);
        Assert.Equal(false, read["BooleanField"]);

        var doc2 = new Document
        {
            ["Int32Field"] = int.MaxValue,
            ["Int64Field"] = long.MinValue,
            ["FloatField"] = float.MaxValue,
            ["DoubleField"] = double.MinValue,
        };
        var reference2 = table.Insert(doc2);
        var read2 = table.Get(reference2);

        Assert.NotNull(read2);
        Assert.Equal(int.MaxValue, read2!["Int32Field"]);
        Assert.Equal(long.MinValue, read2["Int64Field"]);
        Assert.Equal(float.MaxValue, read2["FloatField"]);
        Assert.Equal(double.MinValue, read2["DoubleField"]);
    }

    [Fact]
    public void AllDataTypes_LargeStringAndBytes_RoundTrip()
    {
        using var db = OpenDatabase();
        var table = CreateAllTypesTable(db);

        // Exercise the ushort length-prefix encoding with a large-ish payload
        // (well under the 65535 limit, but big enough to span multiple pages).
        var largeString = new string('A', 5000);
        var largeBytes = new byte[8000];
        new Random(42).NextBytes(largeBytes);

        var doc = new Document
        {
            ["StringField"] = largeString,
            ["BytesField"] = largeBytes,
        };

        var reference = table.Insert(doc);
        var read = table.Get(reference);

        Assert.NotNull(read);
        Assert.Equal(largeString, read!["StringField"]);
        Assert.Equal(largeBytes, read["BytesField"]);
    }

    [Fact]
    public void AllDataTypes_DateTimeEra_NonCommonEra_RoundTrips()
    {
        using var db = OpenDatabase();
        var table = CreateAllTypesTable(db);

        var bce = new DateTimeEra(100, 1, 1, "BCE");
        var doc = new Document { ["DateTimeEraField"] = bce };

        var reference = table.Insert(doc);
        var read = table.Get(reference);

        Assert.NotNull(read);
        var readEra = Assert.IsType<DateTimeEra>(read!["DateTimeEraField"]);
        Assert.Equal(bce.Year, readEra.Year);
        Assert.Equal(bce.Month, readEra.Month);
        Assert.Equal(bce.Day, readEra.Day);
        Assert.Equal("BCE", readEra.Era);
    }
}
