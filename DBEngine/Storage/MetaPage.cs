namespace DBEngine.Storage;

/// <summary>
/// Represents a page storing the schema of a single collection.
/// </summary>
internal class MetaPage(uint pageId) : Page(pageId, PageType.Meta)
{
    /// <summary>Name of the collection this schema describes.</summary>
    internal string CollectionName { get; set; } = string.Empty;

    /// <summary>Whether this container is a structured table or an unstructured collection.</summary>
    internal ContainerType ContainerType { get; set; } = ContainerType.Table;

    /// <summary>ID of the first data page of this collection.</summary>
    internal uint FirstDataPageId { get; set; } = Constants.NoPage;

    /// <summary>ID of the first index page of this collection.</summary>
    internal uint FirstIndexPageId { get; set; } = Constants.NoPage;

    /// <summary>ID of the first history page of this collection.</summary>
    internal uint FirstHistoryPageId { get; set; } = Constants.NoPage;

    /// <summary>The field definitions for this collection.</summary>
    internal List<FieldMeta> Fields { get; init; } = [];

    /// <summary>
    /// Maps the name of each indexed field to the id of the first page of its index chain
    /// (<see cref="Constants.NoPage"/> if the index exists but is currently empty).
    /// </summary>
    internal Dictionary<string, uint> IndexRoots { get; init; } = [];
}
