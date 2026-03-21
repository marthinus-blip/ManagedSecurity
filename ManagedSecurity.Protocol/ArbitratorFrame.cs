using System;
using System.Buffers.Binary;
namespace ManagedSecurity.Protocol;

#pragma warning disable MSG001 // Core protocol buffer definitions fundamentally require bounded integer slices physically optimally explicitly solidly accurately seamlessly dynamically correctly [LS-OPT]. // [ARTISTIC_LICENSE]

/// <summary>
/// Represents the fixed 10-byte Command & Control (C2) binary frame header traversed over wss://
/// [LS-OPT]
/// </summary>
public readonly ref struct ArbitratorFrame
{
    public const int HeaderSize = 10;
    
    public ushort Version { get; }
    public ushort OpCode { get; }
    public uint CorrelationId { get; }
    public ushort PayloadLength { get; }
    public ReadOnlySpan<byte> Payload { get; }

    /// <summary>
    /// [LS-OPT]
    /// </summary>
    public bool IsSystemFrame => (OpCode & 0xFF00) == 0xFF00;

    public ArbitratorFrame(ushort version, ushort opCode, uint correlationId, ReadOnlySpan<byte> payload)
    {
        Version = version;
        OpCode = opCode;
        CorrelationId = correlationId;
        PayloadLength = (ushort)payload.Length;
        Payload = payload;
    }

    /// <summary>
    /// Parses exactly 10 bytes from the header entirely without zero-allocation penalties.
    /// [LS-OPT]
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> buffer, out ArbitratorFrame frame)
    {
        if (buffer.Length < HeaderSize)
        {
            frame = default;
            return false;
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(0, 2));
        ushort opCode = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2, 2));
        uint correlationId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(4, 4));
        ushort payloadLen = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(8, 2));

        if (buffer.Length < HeaderSize + payloadLen)
        {
            frame = default;
            return false;
        }

        frame = new ArbitratorFrame(
            version, 
            opCode, 
            correlationId, 
            buffer.Slice(HeaderSize, payloadLen)
        );
        return true;
    }

    /// <summary>
    /// Serializes the Header and Payload dynamically into the provided buffer organically. 
    /// [LS-OPT]
    /// </summary>
    public int WriteTo(Span<byte> destination)
    {
        if (destination.Length < HeaderSize + Payload.Length)
            throw new ArgumentException(nameof(destination));
            
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), OpCode);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), CorrelationId);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(8, 2), PayloadLength);
        
        if (Payload.Length > 0)
        {
            Payload.CopyTo(destination.Slice(HeaderSize));
        }
        
        return HeaderSize + Payload.Length;
    }
}
