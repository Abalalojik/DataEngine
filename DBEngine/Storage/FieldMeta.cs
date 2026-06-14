namespace DBEngine.Storage;

/// <summary>
/// Describes the metadata of a single field within a collection.
/// </summary>
internal record struct FieldMeta
{
    /// <summary>Name of the field.</summary>
    internal string Name { get; init; }

    /// <summary>Data type of the field.</summary>
    internal FieldType Type { get; init; }

    /// <summary>If true, all changes to this field are permanently tracked.</summary>
    internal bool IsVersioned { get; init; }

    /// <summary>If true, this field must always have a value.</summary>
    internal bool IsRequired { get; init; }

    /// <summary>If true, this field is indexed for faster lookups.</summary>
    internal bool IsIndexed { get; init; }
}
