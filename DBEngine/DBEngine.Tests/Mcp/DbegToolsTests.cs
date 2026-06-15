using dbeg_mcp;
using Xunit;

namespace DBEngine.Tests.Mcp;

/// <summary>
/// Exercises the dbeg-mcp server tools (the externally-exposed surface) end-to-end.
/// DbegTools holds a process-wide static `_db`, so these run in the same non-parallel
/// collection as the engine integration tests; each test opens a temp db and closes it
/// in Dispose so the singleton never leaks between tests.
/// </summary>
public sealed class DbegToolsTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"mcp-test-{Guid.NewGuid():N}.dbeg");

    public void Dispose()
    {
        DbegTools.DbClose();
        try { if (File.Exists(_path)) File.Delete(_path); } catch { /* best effort */ }
    }

    private const string IdNameSchema =
        "[{\"name\":\"Id\",\"type\":\"Guid\",\"required\":true,\"indexed\":true}," +
        "{\"name\":\"Name\",\"type\":\"String\",\"indexed\":true}]";

    [Fact]
    public void Open_Create_Insert_Find_Get_RoundTrips()
    {
        Assert.StartsWith("Opened:", DbegTools.DbOpen(_path));
        Assert.Contains("Created table 'People'", DbegTools.DbCreateContainer("People", "table", IdNameSchema));

        var id = Guid.NewGuid();
        var insertRef = DbegTools.DbInsert("People", $"{{\"Id\":\"{id}\",\"Name\":\"Bob\"}}");
        Assert.Matches(@"^\d+:\d+$", insertRef);   // RecordRef "page:slot"

        // Find by the indexed Name field must return the row we just inserted
        // (this is the exact path that regressed with the stale-meta bug).
        var found = DbegTools.DbFind("People", "Name", "Bob");
        Assert.Contains(insertRef, found);

        var doc = DbegTools.DbGet("People", insertRef);
        Assert.Contains("Bob", doc);
        Assert.Contains(id.ToString(), doc);
    }

    [Fact]
    public void Status_ReflectsOpenThenClosed()
    {
        Assert.Equal("(no database open)", DbegTools.DbStatus());
        DbegTools.DbOpen(_path);
        Assert.StartsWith("Open:", DbegTools.DbStatus());
        DbegTools.DbClose();
        Assert.Equal("(no database open)", DbegTools.DbStatus());
    }

    [Fact]
    public void CreateContainer_IsIdempotent()
    {
        DbegTools.DbOpen(_path);
        DbegTools.DbCreateContainer("Things", "table", IdNameSchema);
        Assert.Contains("already exists", DbegTools.DbCreateContainer("Things", "table", IdNameSchema));
    }

    [Fact]
    public void AddField_ThenSchema_ShowsNewField()
    {
        DbegTools.DbOpen(_path);
        DbegTools.DbCreateContainer("Notes", "table",
            "[{\"name\":\"Id\",\"type\":\"Guid\",\"required\":true,\"indexed\":true}]");
        Assert.Contains("Added field 'Body'", DbegTools.DbAddField("Notes", "Body", "String", false, false));
        Assert.Contains("Body", DbegTools.DbSchema("Notes"));
    }

    [Fact]
    public void Tool_WithNoDatabaseOpen_ReturnsErrorNotCrash()
    {
        // DbInsert wraps errors; with no DB open it should return an error string, not throw.
        Assert.Contains("No database is open", DbegTools.DbInsert("X", "{}"));
    }
}
