namespace DBEngine.Storage;

/// <summary>An archived value of a single field, captured at the moment it was overwritten.</summary>
internal readonly record struct HistoryEntry
{
    /// <summary>The record the archived value belonged to.</summary>
    internal RecordId RecordId { get; init; }

    /// <summary>The field whose previous value was archived.</summary>
    internal string FieldName { get; init; }

    /// <summary>UTC timestamp at which the value was overwritten.</summary>
    internal DateTime TimestampUtc { get; init; }

    /// <summary>The value that was overwritten.</summary>
    internal FieldValue Value { get; init; }
}
