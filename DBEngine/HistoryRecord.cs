namespace DBEngine;

/// <summary>
/// An archived value of a single field, captured at the moment it was overwritten.
/// </summary>
public readonly record struct HistoryRecord
{
    /// <summary>The record the archived value belonged to.</summary>
    public RecordRef RecordRef { get; init; }

    /// <summary>The field whose previous value was archived.</summary>
    public string FieldName { get; init; }

    /// <summary>UTC timestamp at which the value was overwritten.</summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>The value that was overwritten, or null if the field was unset at that time.</summary>
    public object? Value { get; init; }
}
