using System.Text;

namespace DBEngine.Storage;

/// <summary>
/// Reads and writes <see cref="MetaPage"/> instances to and from disk.
/// </summary>
internal static class MetaPageIO
{
    private const byte FlagVersioned = 0x01;
    private const byte FlagRequired = 0x02;
    private const byte FlagIndexed = 0x04;

    /// <summary>Reads the meta page with the given id.</summary>
    internal static MetaPage Read(uint pageId)
    {
        using var reader = new PageReader(pageId);
        var generic = reader.Read() ?? throw new InvalidDataException($"Unable to read meta page {pageId}.");

        var meta = new MetaPage(pageId) { Header = generic.Header };
        var span = generic.Data.Span;
        int offset = 0;

        meta.ContainerType = (ContainerType)span[offset]; offset += 1;
        meta.FirstDataPageId = BitConverter.ToUInt32(span[offset..]); offset += 4;
        meta.FirstIndexPageId = BitConverter.ToUInt32(span[offset..]); offset += 4;
        meta.FirstHistoryPageId = BitConverter.ToUInt32(span[offset..]); offset += 4;

        offset += ReadString(span[offset..], out var collectionName);
        meta.CollectionName = collectionName;

        ushort fieldCount = BitConverter.ToUInt16(span[offset..]); offset += 2;
        for (int i = 0; i < fieldCount; i++)
        {
            offset += ReadString(span[offset..], out var name);
            var type = (FieldType)span[offset]; offset += 1;
            byte flags = span[offset]; offset += 1;

            meta.Fields.Add(new FieldMeta
            {
                Name = name,
                Type = type,
                IsVersioned = (flags & FlagVersioned) != 0,
                IsRequired = (flags & FlagRequired) != 0,
                IsIndexed = (flags & FlagIndexed) != 0
            });
        }

        ushort indexCount = BitConverter.ToUInt16(span[offset..]); offset += 2;
        for (int i = 0; i < indexCount; i++)
        {
            offset += ReadString(span[offset..], out var fieldName);
            uint rootPageId = BitConverter.ToUInt32(span[offset..]); offset += 4;
            meta.IndexRoots[fieldName] = rootPageId;
        }

        return meta;
    }

    /// <summary>Persists <paramref name="meta"/> to its page.</summary>
    internal static void Write(MetaPage meta)
    {
        var span = meta.Data.Span;
        span.Clear();
        int offset = 0;

        span[offset] = (byte)meta.ContainerType; offset += 1;
        BitConverter.TryWriteBytes(span[offset..], meta.FirstDataPageId); offset += 4;
        BitConverter.TryWriteBytes(span[offset..], meta.FirstIndexPageId); offset += 4;
        BitConverter.TryWriteBytes(span[offset..], meta.FirstHistoryPageId); offset += 4;

        offset += WriteString(span[offset..], meta.CollectionName);

        BitConverter.TryWriteBytes(span[offset..], (ushort)meta.Fields.Count); offset += 2;
        foreach (var field in meta.Fields)
        {
            offset += WriteString(span[offset..], field.Name);
            span[offset] = (byte)field.Type; offset += 1;

            byte flags = 0;
            if (field.IsVersioned) flags |= FlagVersioned;
            if (field.IsRequired) flags |= FlagRequired;
            if (field.IsIndexed) flags |= FlagIndexed;
            span[offset] = flags; offset += 1;
        }

        BitConverter.TryWriteBytes(span[offset..], (ushort)meta.IndexRoots.Count); offset += 2;
        foreach (var (fieldName, rootPageId) in meta.IndexRoots)
        {
            offset += WriteString(span[offset..], fieldName);
            BitConverter.TryWriteBytes(span[offset..], rootPageId); offset += 4;
        }

        using var writer = new PageWriter(meta.Header.PageId);
        if (!writer.Write(meta))
            throw new IOException($"Unable to write meta page {meta.Header.PageId}.");
    }

    private static int ReadString(ReadOnlySpan<byte> source, out string value)
    {
        ushort len = BitConverter.ToUInt16(source);
        value = Encoding.UTF8.GetString(source.Slice(2, len));
        return 2 + len;
    }

    private static int WriteString(Span<byte> dest, string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        BitConverter.TryWriteBytes(dest, (ushort)byteCount);
        Encoding.UTF8.GetBytes(value, dest[2..]);
        return 2 + byteCount;
    }
}
