using Xunit;

namespace DBEngine.Tests;

public class DatabaseCrudTests : IDisposable
{
    private readonly string _path;

    public DatabaseCrudTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"dbengine-test-{Guid.NewGuid():N}.dbeg");
    }

    public void Dispose()
    {
        Database.Current?.Dispose();
        if (File.Exists(_path))
            File.Delete(_path);
    }

    private Database OpenDatabase() => Database.Open(_path, replaceIfExists: true);

    [Fact]
    public void Open_RejectsNonDbegExtension()
    {
        var badPath = Path.ChangeExtension(_path, ".db");
        Assert.Throws<ArgumentException>(() => Database.Open(badPath));
    }

    [Fact]
    public void Open_ThrowsIfAnotherDatabaseIsAlreadyOpen()
    {
        using var db = OpenDatabase();
        var otherPath = Path.Combine(Path.GetTempPath(), $"dbengine-test-{Guid.NewGuid():N}.dbeg");

        try
        {
            Assert.Throws<InvalidOperationException>(() => Database.Open(otherPath));
        }
        finally
        {
            if (File.Exists(otherPath))
                File.Delete(otherPath);
        }
    }

    [Fact]
    public void CreateTable_InsertGetUpdateDelete_RoundTrips()
    {
        using var db = OpenDatabase();

        var table = db.CreateTable("People", new[]
        {
            new Field("Name", DataType.String, isRequired: true),
            new Field("Age", DataType.Int32),
            new Field("Active", DataType.Boolean)
        });

        var doc = new Document
        {
            ["Name"] = "Ada Lovelace",
            ["Age"] = 36,
            ["Active"] = true
        };

        var reference = table.Insert(doc);
        Assert.False(reference.IsNone);

        var read = table.Get(reference);
        Assert.NotNull(read);
        Assert.Equal("Ada Lovelace", read!["Name"]);
        Assert.Equal(36, read["Age"]);
        Assert.Equal(true, read["Active"]);

        var updated = new Document
        {
            ["Name"] = "Ada Lovelace",
            ["Age"] = 37,
            ["Active"] = false
        };
        var newRef = table.Update(reference, updated);

        var readBack = table.Get(newRef);
        Assert.NotNull(readBack);
        Assert.Equal(37, readBack!["Age"]);
        Assert.Equal(false, readBack["Active"]);

        Assert.True(table.Delete(newRef));
        Assert.Null(table.Get(newRef));
        Assert.False(table.Delete(newRef));
    }

    [Fact]
    public void CreateTable_RejectsDuplicateFieldNames()
    {
        using var db = OpenDatabase();

        Assert.Throws<ArgumentException>(() => db.CreateTable("Bad", new[]
        {
            new Field("Name", DataType.String),
            new Field("Name", DataType.Int32)
        }));
    }

    [Fact]
    public void CreateTable_RejectsDuplicateContainerNames()
    {
        using var db = OpenDatabase();

        db.CreateTable("People", new[] { new Field("Name", DataType.String) });

        Assert.Throws<InvalidOperationException>(() =>
            db.CreateTable("People", new[] { new Field("Other", DataType.String) }));
    }

    [Fact]
    public void Insert_RequiredFieldMissing_Throws()
    {
        using var db = OpenDatabase();

        var table = db.CreateTable("People", new[]
        {
            new Field("Name", DataType.String, isRequired: true)
        });

        Assert.Throws<ArgumentException>(() => table.Insert(new Document()));
    }

    [Fact]
    public void Update_ArchivesPreviousValuesAsHistory()
    {
        using var db = OpenDatabase();

        var table = db.CreateTable("People", new[]
        {
            new Field("Name", DataType.String, isVersioned: true),
            new Field("Age", DataType.Int32, isVersioned: true)
        });

        var reference = table.Insert(new Document { ["Name"] = "Ada", ["Age"] = 36 });
        table.Update(reference, new Document { ["Name"] = "Ada", ["Age"] = 37 });
        table.Update(reference, new Document { ["Name"] = "Ada Lovelace", ["Age"] = 38 });

        var history = table.GetHistory(reference).ToList();

        Assert.Contains(history, h => h.FieldName == "Age" && Equals(h.Value, 36));
        Assert.Contains(history, h => h.FieldName == "Age" && Equals(h.Value, 37));
        Assert.Contains(history, h => h.FieldName == "Name" && Equals(h.Value, "Ada"));
    }

    [Fact]
    public void Find_And_Range_UseIndexedFields()
    {
        using var db = OpenDatabase();

        var table = db.CreateTable("People", new[]
        {
            new Field("Name", DataType.String, isIndexed: true),
            new Field("Age", DataType.Int32, isIndexed: true)
        });

        var alice = table.Insert(new Document { ["Name"] = "Alice", ["Age"] = 30 });
        var bob = table.Insert(new Document { ["Name"] = "Bob", ["Age"] = 25 });
        var carol = table.Insert(new Document { ["Name"] = "Carol", ["Age"] = 40 });

        var foundByName = table.Find("Name", "Bob").ToList();
        Assert.Single(foundByName);
        Assert.Equal(bob, foundByName[0]);

        var byAge = table.Range("Age", 28, 40).ToList();
        Assert.Contains(alice, byAge);
        Assert.Contains(carol, byAge);
        Assert.DoesNotContain(bob, byAge);
    }

    [Fact]
    public void All_EnumeratesEveryInsertedRecord()
    {
        using var db = OpenDatabase();

        var table = db.CreateTable("People", new[] { new Field("Name", DataType.String) });

        table.Insert(new Document { ["Name"] = "Alice" });
        table.Insert(new Document { ["Name"] = "Bob" });
        table.Insert(new Document { ["Name"] = "Carol" });

        var names = table.All().Select(x => (string)x.Document["Name"]!).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "Alice", "Bob", "Carol" }, names);
    }

    [Fact]
    public void CreateCollection_AllowsLooselySchemaedDocuments()
    {
        using var db = OpenDatabase();

        var collection = db.CreateCollection("Notes", new[]
        {
            new Field("Title", DataType.String),
            new Field("Body", DataType.String)
        });

        var withBoth = collection.Insert(new Document { ["Title"] = "Hello", ["Body"] = "World" });
        var titleOnly = collection.Insert(new Document { ["Title"] = "Just a title" });

        Assert.Equal("World", collection.Get(withBoth)!["Body"]);
        Assert.Null(collection.Get(titleOnly)!["Body"]);
    }

    [Fact]
    public void ListContainers_And_DropContainer_Work()
    {
        using var db = OpenDatabase();

        db.CreateTable("People", new[] { new Field("Name", DataType.String) });
        db.CreateCollection("Notes", new[] { new Field("Title", DataType.String) });

        var containers = db.ListContainers().ToList();
        Assert.Contains(containers, c => c.Name == "People" && c.Kind == ContainerKind.Table);
        Assert.Contains(containers, c => c.Name == "Notes" && c.Kind == ContainerKind.Collection);

        db.DropContainer("Notes");

        containers = db.ListContainers().ToList();
        Assert.DoesNotContain(containers, c => c.Name == "Notes");
        Assert.Null(db.GetCollection("Notes"));
    }

    [Fact]
    public void DropContainer_UnknownName_Throws()
    {
        using var db = OpenDatabase();
        Assert.Throws<ArgumentException>(() => db.DropContainer("DoesNotExist"));
    }

    [Fact]
    public void GetTable_ReturnsNullForCollectionName()
    {
        using var db = OpenDatabase();
        db.CreateCollection("Notes", new[] { new Field("Title", DataType.String) });

        Assert.Null(db.GetTable("Notes"));
        Assert.NotNull(db.GetCollection("Notes"));
    }

    [Fact]
    public void TableRef_FieldRoundTripsAsRecordRef()
    {
        using var db = OpenDatabase();

        var authors = db.CreateTable("Authors", new[] { new Field("Name", DataType.String) });
        var books = db.CreateTable("Books", new[]
        {
            new Field("Title", DataType.String),
            new Field("Author", DataType.TableRef)
        });

        var author = authors.Insert(new Document { ["Name"] = "Ada Lovelace" });
        var book = books.Insert(new Document { ["Title"] = "Notes on the Analytical Engine", ["Author"] = author });

        var readBack = books.Get(book)!;
        var authorRef = Assert.IsType<RecordRef>(readBack["Author"]);
        Assert.Equal(author, authorRef);

        var resolvedAuthor = authors.Get(authorRef);
        Assert.Equal("Ada Lovelace", resolvedAuthor!["Name"]);
    }

    [Fact]
    public void LargeStringValue_OverflowsAcrossPages()
    {
        using var db = OpenDatabase();

        var table = db.CreateTable("Docs", new[] { new Field("Body", DataType.String) });

        var bigText = new string('x', 50_000);
        var reference = table.Insert(new Document { ["Body"] = bigText });

        var readBack = table.Get(reference)!;
        Assert.Equal(bigText, readBack["Body"]);
    }
}
