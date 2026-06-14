using System.Collections;

namespace DBEngine;

/// <summary>
/// A bag of field values for a single row (table) or document (collection).
/// Values are plain CLR objects; their expected runtime type depends on the corresponding
/// field's <see cref="DataType"/>:
/// <list type="bullet">
/// <item>String, Enum -> <see cref="string"/></item>
/// <item>Int32 -> <see cref="int"/></item>
/// <item>Int64 -> <see cref="long"/></item>
/// <item>Float -> <see cref="float"/></item>
/// <item>Double -> <see cref="double"/></item>
/// <item>Boolean -> <see cref="bool"/></item>
/// <item>DateTime -> <see cref="DateTime"/></item>
/// <item>DateTimeEra -> <see cref="Temporal.DateTimeEra"/></item>
/// <item>Guid -> <see cref="Guid"/></item>
/// <item>Bytes -> <c>byte[]</c></item>
/// <item>TableRef, CollectionRef -> <see cref="RecordRef"/></item>
/// </list>
/// A value of <c>null</c> represents an unset/NULL field.
/// </summary>
public class Document : IEnumerable<KeyValuePair<string, object?>>
{
    private readonly Dictionary<string, object?> _values;

    /// <summary>Creates an empty document.</summary>
    public Document() => _values = new Dictionary<string, object?>();

    /// <summary>Creates a document pre-populated with the given values.</summary>
    public Document(IEnumerable<KeyValuePair<string, object?>> values) =>
        _values = new Dictionary<string, object?>(values);

    /// <summary>Gets or sets the value of the given field. Returns null if the field is unset.</summary>
    public object? this[string fieldName]
    {
        get => _values.TryGetValue(fieldName, out var value) ? value : null;
        set => _values[fieldName] = value;
    }

    /// <summary>The names of every field currently set on this document.</summary>
    public IEnumerable<string> Fields => _values.Keys;

    /// <summary>Returns true if the given field has been set on this document (even if set to null).</summary>
    public bool Contains(string fieldName) => _values.ContainsKey(fieldName);

    /// <summary>Removes a field from this document.</summary>
    public bool Remove(string fieldName) => _values.Remove(fieldName);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
