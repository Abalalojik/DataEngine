namespace DBEngine;

/// <summary>Distinguishes tables from collections when listing a database's containers.</summary>
public enum ContainerKind
{
    /// <summary>A strictly-schemaed table.</summary>
    Table,

    /// <summary>A loosely-schemaed collection of documents.</summary>
    Collection
}
