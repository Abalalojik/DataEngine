namespace DBEngine;

/// <summary>
/// Public data types supported for table and collection fields.
/// Mirrors <c>DBEngine.Storage.FieldType</c>, which is internal to the storage layer.
/// </summary>
public enum DataType
{
    /// <summary>A UTF-8 text value.</summary>
    String,
    /// <summary>A 32-bit signed integer.</summary>
    Int32,
    /// <summary>A 64-bit signed integer.</summary>
    Int64,
    /// <summary>A 32-bit floating point number.</summary>
    Float,
    /// <summary>A 64-bit floating point number.</summary>
    Double,
    /// <summary>A boolean value (true or false).</summary>
    Boolean,
    /// <summary>A standard date and time value (UTC).</summary>
    DateTime,
    /// <summary>A date and time value supporting alternative calendar systems.</summary>
    DateTimeEra,
    /// <summary>A globally unique identifier.</summary>
    Guid,
    /// <summary>Raw binary data.</summary>
    Bytes,
    /// <summary>A named enumeration value, stored as its name.</summary>
    Enum,
    /// <summary>A reference to a row in a table.</summary>
    TableRef,
    /// <summary>A reference to a document in a collection.</summary>
    CollectionRef
}
