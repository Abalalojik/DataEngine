using DBEngine.Storage;

namespace DBEngine;

/// <summary>
/// A strictly-schemaed table: every row conforms to the field definitions the table was created with.
/// </summary>
public sealed class Table : ContainerBase
{
    internal Table(MetaPage meta) : base(meta)
    {
    }
}
