using DBEngine.Storage;

namespace DBEngine;

/// <summary>
/// Main entry point for DBEngine. Only one database can be open at a time.
/// </summary>
public class Database : IDisposable
{
    private static Database? _current;
    private static readonly Lock _lock = new();

    /// <summary>The currently open database instance, or null if none is open.</summary>
    public static Database? Current => _current;

    /// <summary>Absolute path to the database file.</summary>
    public string FilePath { get; }

    private bool _disposed;
    private readonly System.Collections.Concurrent.ConcurrentBag<FileStream> _streamPool = new();

    internal FileStream RentStream()
    {
        if (_streamPool.TryTake(out var fs))
            return fs;
        return new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite, Constants.PageSize, false);
    }

    internal void ReturnStream(FileStream fs)
    {
        if (!_disposed)
            _streamPool.Add(fs);
        else
            fs.Dispose();
    }

    private Database(string filePath)
    {
        FilePath = filePath;
    }

    /// <summary>
    /// Opens a database file. Only one database can be open at a time.
    /// </summary>
    /// <param name="filePath">Path to the .dbeg file.</param>
    /// <param name="replaceIfExists">If true, overwrites an existing file. Defaults to false.</param>
    /// <returns>The opened database instance.</returns>
    public static Database Open(string filePath, bool replaceIfExists = false)
    {
        lock (_lock)
        {
            if (_current != null)
                throw new InvalidOperationException(
                    "A database is already open. Call Database.Current.Dispose() before opening a new one.");

            // Validate path
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException(
                    "Database file path cannot be null or empty.", nameof(filePath));

            if (!Path.GetExtension(filePath).Equals(".dbeg", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    "Database file must have the .dbeg extension.", nameof(filePath));

            string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (directory != null && !Directory.Exists(directory))
                throw new DirectoryNotFoundException(
                    $"The directory '{directory}' does not exist.");

            // Handle existing file
            if (File.Exists(filePath))
            {
                if (!replaceIfExists)
                    ValidateMagicNumber(filePath);
                else
                    File.Delete(filePath);
            }

            var db = new Database(Path.GetFullPath(filePath));

            _current = db;

            try
            {
                if (!File.Exists(filePath))
                    db.Initialize();
            }
            catch
            {
                _current = null;
                db.Dispose();
                throw;
            }

            return db;
        }
    }

    /// <summary>
    /// Verifies that an existing file is a valid DBEngine database by reading the magic
    /// bytes from the RootPage data region (offset 0 within the page data, after the header).
    /// </summary>
    private static void ValidateMagicNumber(string filePath)
    {
        // Magic lives at: PageHeaderSize bytes (header) into the file
        Span<byte> buffer = stackalloc byte[4];
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        fs.Seek(Constants.PageHeaderSize, SeekOrigin.Begin);
        int read = fs.Read(buffer);

        if (read < 4 || !buffer.SequenceEqual(Constants.MagicNumber))
            throw new InvalidDataException(
                $"The file '{filePath}' is not a valid DBEngine database file.");
    }

    /// <summary>
    /// Writes an empty RootPage (with magic bytes embedded in its data region) to a new database file.
    /// </summary>
    private void Initialize()
    {
        var root = new RootPage();
        // Embed magic in the first 4 bytes of the page data region so it survives page writes
        Constants.MagicNumber.CopyTo(root.Data.Span);
        using var writer = new PageWriter(0);
        writer.Write(root);
    }

    /// <summary>Creates a new strictly-schemaed table and returns a handle to it.</summary>
    /// <param name="name">The name of the table. Must be unique within the database.</param>
    /// <param name="fields">The table's field definitions, in declaration order.</param>
    public Table CreateTable(string name, IEnumerable<Field> fields)
    {
        var meta = StorageManager.CreateContainer(name, ContainerType.Table, ToFieldMetaList(fields));
        return new Table(meta);
    }

    /// <summary>Creates a new loosely-schemaed collection and returns a handle to it.</summary>
    /// <param name="name">The name of the collection. Must be unique within the database.</param>
    /// <param name="fields">The collection's field definitions, in declaration order.</param>
    public Collection CreateCollection(string name, IEnumerable<Field> fields)
    {
        var meta = StorageManager.CreateContainer(name, ContainerType.Collection, ToFieldMetaList(fields));
        return new Collection(meta);
    }

    /// <summary>Returns a handle to an existing table, or null if no table with that name exists.</summary>
    public Table? GetTable(string name)
    {
        var meta = StorageManager.GetContainer(name);
        return meta is null || meta.ContainerType != ContainerType.Table ? null : new Table(meta);
    }

    /// <summary>Returns a handle to an existing collection, or null if no collection with that name exists.</summary>
    public Collection? GetCollection(string name)
    {
        var meta = StorageManager.GetContainer(name);
        return meta is null || meta.ContainerType != ContainerType.Collection ? null : new Collection(meta);
    }

    /// <summary>Lists the name and kind of every table and collection in the database.</summary>
    public IEnumerable<(string Name, ContainerKind Kind)> ListContainers()
        => StorageManager.ListContainers().Select(m => (
            m.CollectionName,
            m.ContainerType == ContainerType.Table ? ContainerKind.Table : ContainerKind.Collection));

    /// <summary>
    /// Permanently deletes a table or collection, including all of its data, indexes and history.
    /// </summary>
    /// <exception cref="ArgumentException">No container with that name exists.</exception>
    public void DropContainer(string name) => StorageManager.DropContainer(name);

    /// <summary>
    /// Appends a new field to an existing container and migrates all records to include a NULL
    /// value for the new field. Safe to call on a non-empty container.
    /// </summary>
    public void AddField(string containerName, Field field)
        => StorageManager.AddField(containerName, DataTypeMapper.ToFieldMeta(field));

    /// <summary>
    /// Removes a field from an existing container, frees its index (if any), and re-encodes
    /// all records without the dropped field.
    /// </summary>
    public void DropField(string containerName, string fieldName)
        => StorageManager.DropField(containerName, fieldName);

    /// <summary>
    /// Renames a field in an existing container. Only the schema meta page is updated —
    /// no record data is rewritten because records are encoded positionally.
    /// </summary>
    public void RenameField(string containerName, string oldName, string newName)
        => StorageManager.RenameField(containerName, oldName, newName);

    private static List<FieldMeta> ToFieldMetaList(IEnumerable<Field> fields)
    {
        var list = new List<FieldMeta>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var meta = DataTypeMapper.ToFieldMeta(field);
            if (!names.Add(meta.Name))
                throw new ArgumentException($"Duplicate field name '{meta.Name}'.", nameof(fields));
            list.Add(meta);
        }
        return list;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            if (_current == this)
                _current = null;
        }

        while (_streamPool.TryTake(out var fs))
        {
            fs.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}