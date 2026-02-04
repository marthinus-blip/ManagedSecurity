using System;
using System.Buffers.Binary;
using System.Text;

namespace ManagedSecurity.Common;

/// <summary>
/// Represents the unencrypted "Discovery Header" at the start of a ManagedSecurity stream.
/// Designed for high-speed indexing and searching without cryptographic keys.
/// </summary>
public readonly ref struct MasterHeader
{
    public const int FixedSize = 22;
    public static ReadOnlySpan<byte> Magic => "MSG"u8;

    private readonly ReadOnlySpan<byte> _data;

    public MasterHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < FixedSize)
            throw new ArgumentException("Data too short for MasterHeader.");
        
        if (!data.Slice(0, 3).SequenceEqual(Magic))
            throw new ArgumentException("Invalid MasterHeader magic.");

        _data = data;
    }

    public byte Version => _data[3];
    public int ChunkSize => BinaryPrimitives.ReadInt32BigEndian(_data.Slice(4));
    public int KeyIndex => BinaryPrimitives.ReadInt32BigEndian(_data.Slice(8));
    public ushort MetadataLength => BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(12));
    public ulong SeekTableOffset => BinaryPrimitives.ReadUInt64BigEndian(_data.Slice(14));

    public ReadOnlySpan<byte> GetMetadata(ReadOnlySpan<byte> fullData)
    {
        if (MetadataLength == 0) return ReadOnlySpan<byte>.Empty;
        return fullData.Slice(FixedSize, MetadataLength);
    }

    /// <summary>
    /// Writes a master header to the provided span.
    /// </summary>
    public static void Write(Span<byte> destination, byte version, int chunkSize, int keyIndex, ushort metadataLength, ulong seekTableOffset = 0)
    {
        if (destination.Length < FixedSize)
            throw new ArgumentException("Destination span too small.");

        Magic.CopyTo(destination);
        destination[3] = version;
        BinaryPrimitives.WriteInt32BigEndian(destination.Slice(4), chunkSize);
        BinaryPrimitives.WriteInt32BigEndian(destination.Slice(8), keyIndex);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(12), metadataLength);
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(14), seekTableOffset);
    }
}
