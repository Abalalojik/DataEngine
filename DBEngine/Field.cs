namespace DBEngine;

/// <summary>
/// Describes a single field in a table or collection schema.
/// </summary>
public class Field
{
    /// <summary>Name of the field.</summary>
    public string Name { get; }

    /// <summary>Data type of the field.</summary>
    public DataType DataType { get; }

    /// <summary>If true, all previous values of this field are permanently archived on update.</summary>
    public bool IsVersioned { get; }

    /// <summary>If true, this field must always have a non-null value.</summary>
    public bool IsRequired { get; }

    /// <summary>If true, this field is indexed for faster lookups via <see cref="Table.Find"/> and <see cref="Table.Range"/>.</summary>
    public bool IsIndexed { get; }

    public Field(string name, DataType dataType, bool isVersioned = false, bool isRequired = false, bool isIndexed = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Field name cannot be null or empty.", nameof(name));

        Name = name;
        DataType = dataType;
        IsVersioned = isVersioned;
        IsRequired = isRequired;
        IsIndexed = isIndexed;
    }
}
