using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using DBEngine;
using ModelContextProtocol.Server;

namespace dbeg_mcp;

[McpServerToolType]
public static class DbegTools
{
    private static Database? _db;
    private static readonly object _lock = new();

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented    = true,
        Encoder          = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ── Connection ─────────────────────────────────────────────────────────

    [McpServerTool, Description("Open a .dbeg database file. Only one database can be open at a time.")]
    public static string DbOpen(
        [Description("Absolute path to the .dbeg file.")] string path)
    {
        lock (_lock)
        {
            _db?.Dispose();
            _db = Database.Open(path);
            return $"Opened: {_db.FilePath}";
        }
    }

    [McpServerTool, Description("Close the currently open database.")]
    public static string DbClose()
    {
        lock (_lock)
        {
            if (_db == null) return "No database is open.";
            var p = _db.FilePath;
            _db.Dispose();
            _db = null;
            return $"Closed: {p}";
        }
    }

    [McpServerTool, Description("Return the path of the open database, or null if none is open.")]
    public static string DbStatus()
        => _db == null ? "(no database open)" : $"Open: {_db.FilePath}";

    // ── Schema ─────────────────────────────────────────────────────────────

    [McpServerTool, Description("List all tables and collections in the open database.")]
    public static string DbListContainers()
    {
        RequireDb();
        var rows = _db!.ListContainers()
            .Select(c => new { c.Name, Kind = c.Kind.ToString() })
            .ToList();
        return Serialize(rows);
    }

    [McpServerTool, Description("Return the field schema of a named table or collection.")]
    public static string DbSchema(
        [Description("Table or collection name.")] string name)
    {
        RequireDb();
        var t = _db!.GetTable(name);
        if (t != null) return Serialize(t.Schema.Select(FieldInfo));
        var c = _db.GetCollection(name);
        if (c != null) return Serialize(c.Schema.Select(FieldInfo));
        return $"No container named '{name}'.";
    }

    // ── Read ───────────────────────────────────────────────────────────────

    [McpServerTool, Description("Scan all records in a table or collection. Returns JSON array of {ref, doc}.")]
    public static string DbScan(
        [Description("Table or collection name.")] string name,
        [Description("Max records to return (0 = all).")] int limit = 0)
    {
        RequireDb();
        var results = ScanContainer(name, limit);
        return Serialize(results);
    }

    [McpServerTool, Description("Find records by exact indexed-field match. Returns JSON array of RecordRef strings.")]
    public static string DbFind(
        [Description("Table or collection name.")] string name,
        [Description("Indexed field name.")] string field,
        [Description("Value to match (always as a string; GUIDs as '00000000-...').")] string value)
    {
        RequireDb();
        var container = GetContainerBase(name);
        if (container == null) return $"No container named '{name}'.";

        object typed = CoerceValue(container, field, value);
        var refs = container.Find(field, typed).Select(r => r.ToString()).ToList();
        return Serialize(refs);
    }

    [McpServerTool, Description("Get a single record by its RecordRef string.")]
    public static string DbGet(
        [Description("Table or collection name.")] string name,
        [Description("RecordRef string as returned by db_scan or db_insert.")] string recordRef)
    {
        RequireDb();
        var container = GetContainerBase(name);
        if (container == null) return $"No container named '{name}'.";
        var doc = container.Get(ParseRef(recordRef));
        return doc == null ? "null" : Serialize(DocToObj(doc));
    }

    // ── Write ──────────────────────────────────────────────────────────────

    [McpServerTool, Description("Insert a record into a table or collection. Pass a JSON object whose keys match the schema.")]
    public static string DbInsert(
        [Description("Table or collection name.")] string name,
        [Description("JSON object of field values, e.g. {\"Id\":\"<guid>\",\"Name\":\"Hero\"}")] string jsonDoc)
    {
        try
        {
            RequireDb();
            var container = GetContainerBase(name);
            if (container == null) return $"No container named '{name}'.";
            var doc = ParseDoc(container, jsonDoc);
            var r = container.Insert(doc);
            return r.ToString();
        }
        catch (Exception ex) { return FormatError(ex); }
    }

    [McpServerTool, Description("Update an existing record. All fields not present in jsonDoc are cleared to NULL.")]
    public static string DbUpdate(
        [Description("Table or collection name.")] string name,
        [Description("RecordRef string of the record to update.")] string recordRef,
        [Description("JSON object of new field values.")] string jsonDoc)
    {
        try
        {
            RequireDb();
            var container = GetContainerBase(name);
            if (container == null) return $"No container named '{name}'.";
            var doc = ParseDoc(container, jsonDoc);
            var newRef = container.Update(ParseRef(recordRef), doc);
            return newRef.ToString();
        }
        catch (Exception ex) { return FormatError(ex); }
    }

    [McpServerTool, Description("Delete a record by its RecordRef string.")]
    public static string DbDelete(
        [Description("Table or collection name.")] string name,
        [Description("RecordRef string of the record to delete.")] string recordRef)
    {
        try
        {
            RequireDb();
            var container = GetContainerBase(name);
            if (container == null) return $"No container named '{name}'.";
            bool ok = container.Delete(ParseRef(recordRef));
            return ok ? "Deleted." : "Record not found.";
        }
        catch (Exception ex) { return FormatError(ex); }
    }

    [McpServerTool, Description("Permanently drop a table or collection and all its data. Use with care.")]
    public static string DbDrop(
        [Description("Table or collection name to drop.")] string name)
    {
        try
        {
            RequireDb();
            _db!.DropContainer(name);
            return $"Dropped: {name}";
        }
        catch (Exception ex) { return FormatError(ex); }
    }

    // ── Schema alteration ──────────────────────────────────────────────────

    [McpServerTool, Description(
        "Create a new table or collection with the given field schema. Idempotent: if a " +
        "container with that name already exists it is left untouched. 'kind' is 'table' or " +
        "'collection'. 'fields' is a JSON array of field defs: " +
        "[{\"name\":\"Id\",\"type\":\"Guid\",\"required\":true,\"indexed\":true}, ...]. " +
        "type must be one of: String, Int32, Int64, Float, Double, Boolean, DateTime, Guid, Bytes.")]
    public static string DbCreateContainer(
        [Description("Name for the new container.")] string name,
        [Description("'table' or 'collection'.")] string kind,
        [Description("JSON array of {name,type,required,indexed} field definitions.")] string fields)
    {
        try
        {
            RequireDb();
            if (_db!.GetTable(name) != null || _db.GetCollection(name) != null)
                return $"Container '{name}' already exists; left untouched.";

            var arr = JsonNode.Parse(fields)?.AsArray()
                ?? throw new ArgumentException("fields must be a JSON array.");

            var fieldList = new List<Field>();
            foreach (var item in arr)
            {
                var o = item?.AsObject() ?? throw new ArgumentException("Each field must be a JSON object.");
                var fname = o["name"]?.GetValue<string>()
                    ?? throw new ArgumentException("Each field needs a 'name'.");
                var dt = Enum.Parse<DataType>(
                    o["type"]?.GetValue<string>() ?? throw new ArgumentException($"Field '{fname}' needs a 'type'."),
                    ignoreCase: true);
                bool req = o["required"]?.GetValue<bool>() ?? false;
                bool idx = o["indexed"]?.GetValue<bool>() ?? false;
                fieldList.Add(new Field(fname, dt, isRequired: req, isIndexed: idx));
            }
            if (fieldList.Count == 0) throw new ArgumentException("At least one field is required.");

            bool isCollection = kind.Equals("collection", StringComparison.OrdinalIgnoreCase);
            if (isCollection) _db.CreateCollection(name, fieldList);
            else              _db.CreateTable(name, fieldList);

            return $"Created {(isCollection ? "collection" : "table")} '{name}' with {fieldList.Count} field(s).";
        }
        catch (Exception ex) { return FormatError(ex); }
    }

    [McpServerTool, Description(
        "Add a new field to an existing table or collection. " +
        "All existing records are migrated to include NULL for the new field. " +
        "dataType must be one of: String, Int32, Int64, Float, Double, Boolean, DateTime, Guid, Bytes.")]
    public static string DbAddField(
        [Description("Table or collection name.")] string name,
        [Description("Name of the new field.")] string fieldName,
        [Description("Data type: String | Int32 | Int64 | Float | Double | Boolean | DateTime | Guid | Bytes")] string dataType,
        [Description("Whether the field is required (default false).")] bool isRequired = false,
        [Description("Whether the field should be indexed (default false).")] bool isIndexed = false)
    {
        try
        {
            RequireDb();
            var dt = Enum.Parse<DataType>(dataType, ignoreCase: true);
            _db!.AddField(name, new Field(fieldName, dt, isRequired: isRequired, isIndexed: isIndexed));
            return $"Added field '{fieldName}' ({dataType}) to '{name}'.";
        }
        catch (Exception ex) { return FormatError(ex); }
    }

    [McpServerTool, Description(
        "Drop a field from an existing table or collection. " +
        "All existing records are re-encoded without the field and its index is freed.")]
    public static string DbDropField(
        [Description("Table or collection name.")] string name,
        [Description("Name of the field to drop.")] string fieldName)
    {
        try
        {
            RequireDb();
            _db!.DropField(name, fieldName);
            return $"Dropped field '{fieldName}' from '{name}'.";
        }
        catch (Exception ex) { return FormatError(ex); }
    }

    [McpServerTool, Description(
        "Rename a field in an existing table or collection. " +
        "Only the schema is updated — no record data is rewritten.")]
    public static string DbRenameField(
        [Description("Table or collection name.")] string name,
        [Description("Current field name.")] string oldName,
        [Description("New field name.")] string newName)
    {
        try
        {
            RequireDb();
            _db!.RenameField(name, oldName, newName);
            return $"Renamed field '{oldName}' → '{newName}' in '{name}'.";
        }
        catch (Exception ex) { return FormatError(ex); }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void RequireDb()
    {
        if (_db == null) throw new InvalidOperationException("No database is open. Call db_open first.");
    }

    private static ContainerBase? GetContainerBase(string name)
    {
        ContainerBase? c = _db!.GetTable(name);
        c ??= _db.GetCollection(name);
        return c;
    }

    private static IEnumerable<object> ScanContainer(string name, int limit)
    {
        var container = GetContainerBase(name)
            ?? throw new ArgumentException($"No container named '{name}'.");

        int count = 0;
        foreach (var (r, doc) in container.All())
        {
            yield return new { @ref = r.ToString(), doc = DocToObj(doc) };
            if (limit > 0 && ++count >= limit) yield break;
        }
    }

    private static Dictionary<string, object?> DocToObj(Document doc)
    {
        var d = new Dictionary<string, object?>();
        foreach (var kv in doc)
            d[kv.Key] = kv.Value switch
            {
                Guid g   => g.ToString(),
                DateTime dt => dt.ToString("O"),
                _           => kv.Value,
            };
        return d;
    }

    private static Document ParseDoc(ContainerBase container, string json)
    {
        var node = JsonNode.Parse(json)?.AsObject()
            ?? throw new ArgumentException("jsonDoc must be a JSON object.");

        var doc = new Document();
        foreach (var (key, val) in node)
        {
            var field = container.Schema.FirstOrDefault(f => f.Name == key);
            if (field == null) continue;

            doc[key] = field.DataType switch
            {
                DataType.Guid     => val != null ? Guid.Parse(val.GetValue<string>()) : (object?)null,
                DataType.Int32    => val?.GetValue<int>(),
                DataType.Int64    => val?.GetValue<long>(),
                DataType.Boolean  => val?.GetValue<bool>(),
                DataType.DateTime => val != null ? DateTime.Parse(val.GetValue<string>()) : (object?)null,
                _                 => val?.GetValue<string>(),
            };
        }
        return doc;
    }

    private static object CoerceValue(ContainerBase container, string fieldName, string value)
    {
        var field = container.Schema.FirstOrDefault(f => f.Name == fieldName)
            ?? throw new ArgumentException($"No field '{fieldName}' in container.");

        return field.DataType switch
        {
            DataType.Guid  => Guid.Parse(value),
            DataType.Int32 => int.Parse(value),
            DataType.Int64 => long.Parse(value),
            _              => value,
        };
    }

    private static RecordRef ParseRef(string s)
    {
        // RecordRef.ToString() → "PageId:Slot"
        var parts = s.Split(':');
        if (parts.Length != 2 || !uint.TryParse(parts[0], out var page) || !ushort.TryParse(parts[1], out var slot))
            throw new ArgumentException($"Invalid RecordRef '{s}'. Expected format: '<pageId>:<slot>' as returned by db_scan or db_insert.");
        return new RecordRef(page, slot);
    }

    private static object FieldInfo(Field f) => new
    {
        f.Name,
        Type       = f.DataType.ToString(),
        f.IsRequired,
        f.IsIndexed,
        f.IsVersioned,
    };

    private static string Serialize(object obj)
        => JsonSerializer.Serialize(obj, _json);

    private static string FormatError(Exception ex)
        => $"ERROR [{ex.GetType().Name}]: {ex.Message}\n{ex.StackTrace}";
}
