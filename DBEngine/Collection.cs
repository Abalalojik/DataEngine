using DBEngine.Storage;

namespace DBEngine;

/// <summary>
/// A loosely-schemaed collection of documents grouped by a common subject.
/// Declared fields behave the same as on a <see cref="Table"/>, but <see cref="Field.IsRequired"/>
/// is typically left false so documents may omit fields freely.
/// </summary>
public sealed class Collection : ContainerBase
{
    internal Collection(MetaPage meta) : base(meta)
    {
    }
}
