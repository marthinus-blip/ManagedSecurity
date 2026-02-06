using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace ManagedSecurity.Common;

public readonly record struct SeekPoint(uint RelativeTimestampMs, ulong FileOffset, uint FrameIndex);

public static class SeekTableSerializer
{
    public static ReadOnlySpan<byte> Magic => "SEEK"u8;

    public static byte[] Serialize(IEnumerable<SeekPoint> points)
    {
        var list = points is List<SeekPoint> l ? l : new List<SeekPoint>(points);
        // 6 byte header + 16 bytes per point (4 TS + 8 Offset + 4 FrameIndex)
        byte[] data = new byte[6 + (list.Count * 16)];
        
        Magic.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), (ushort)list.Count);
        
        int offset = 6;
        foreach (var p in list)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), p.RelativeTimestampMs);
            BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(offset + 4), p.FileOffset);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset + 12), p.FrameIndex);
            offset += 16;
        }
        
        return data;
    }

    public static List<SeekPoint> Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6) return new List<SeekPoint>();
        if (!data.Slice(0, 4).SequenceEqual(Magic)) throw new ArgumentException("Invalid SeekTable magic.");
        
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4));
        var points = new List<SeekPoint>(count);
        
        int offset = 6;
        for (int i = 0; i < count; i++)
        {
            if (offset + 16 > data.Length) break;
            
            uint ts = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset));
            ulong fo = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(offset + 4));
            uint fi = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 12));
            points.Add(new SeekPoint(ts, fo, fi));
            offset += 16;
        }
        
        return points;
    }
}
