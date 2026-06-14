namespace DBEngine.Storage;

/// <summary>
/// Defines whether a metadata page describes a table or a collection.
/// </summary>
internal enum ContainerType : byte
{
    /// <summary>A structured container with a defined schema.</summary>
    Table      = 0,
    /// <summary>A flexible container of unstructured documents grouped by a common subject.</summary>
    Collection = 1
}
