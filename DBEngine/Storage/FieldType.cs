namespace DBEngine.Storage;

/// <summary>
/// Defines the supported data types for collection fields.
/// </summary>
internal enum FieldType : byte
{
    /// <summary>A UTF-8 text value.</summary>
    String      = 0,
    /// <summary>A 32-bit signed integer.</summary>
    Int32       = 1,
    /// <summary>A 64-bit signed integer.</summary>
    Int64       = 2,
    /// <summary>A 32-bit floating point number.</summary>
    Float       = 3,
    /// <summary>A 64-bit floating point number.</summary>
    Double      = 4,
    /// <summary>A boolean value (true or false).</summary>
    Boolean     = 5,
    /// <summary>A standard date and time value (UTC).</summary>
    DateTime    = 6,
    /// <summary>A date and time value supporting alternative calendar systems.</summary>
    DateTimeEra = 7,
    /// <summary>A globally unique identifier.</summary>
    Guid        = 8,
    /// <summary>Raw binary data.</summary>
    Bytes       = 9,
    /// <summary>A named enumeration value.</summary>
    Enum        = 10,
    /// <summary>A reference to a row in a table.</summary>
    TableRef      = 11,
    /// <summary>A reference to a collection of documents.</summary>
    CollectionRef = 12
}
