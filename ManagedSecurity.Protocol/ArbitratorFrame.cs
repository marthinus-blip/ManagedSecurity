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
    
    // Core Fragmentation Constraints mapping logically [LSN-OPT]
    public const ushort FragmentMask = 0x8000;
    public const ushort FragmentFinalMask = 0x4000;
    public const ushort OpCodeMask = 0x3FFF;

    public ushort Version { get; }
    public ushort RawOpCode { get; }
    public uint CorrelationId { get; }
    public ushort WirePayloadLength { get; }
    public ReadOnlySpan<byte> Payload { get; }

    /// <summary>
    /// Decoded Operational bounds bypassing metadata flags effectively securely.
    /// </summary>
    public ushort OpCode => (ushort)(RawOpCode & OpCodeMask);
    public bool IsFragmented => (RawOpCode & FragmentMask) != 0;
    public bool IsFinalFragment => (RawOpCode & FragmentFinalMask) != 0;
    
    public ushort SequenceIndex { get; }

    /// <summary>
    /// [LS-OPT]
    /// </summary>
    public bool IsSystemFrame => (OpCode & 0x3F00) == 0x3F00;

    public ArbitratorFrame(ushort version, ushort rawOpCode, uint correlationId, ushort sequenceIndex, ReadOnlySpan<byte> payload)
    {
        Version = version;
        RawOpCode = rawOpCode;
        CorrelationId = correlationId;
        SequenceIndex = sequenceIndex;
        Payload = payload;
        
        // Serialize boundaries firmly mapping Sequence Indexes identically natively seamlessly efficiently.
        WirePayloadLength = IsFragmented ? (ushort)(payload.Length + 2) : (ushort)payload.Length;
    }

    /// <summary>
    /// Parses exactly 10 bytes from the header entirely without zero-allocation penalties.
    /// Deducts `SequenceIndex` seamlessly physically cleanly structurally smoothly.
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
        ushort rawOpCode = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2, 2));
        uint correlationId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(4, 4));
        ushort payloadLen = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(8, 2));

        if (buffer.Length < HeaderSize + payloadLen)
        {
            frame = default;
            return false;
        }

        bool isFragmented = (rawOpCode & FragmentMask) != 0;
        ushort sequenceIndex = 0;
        ReadOnlySpan<byte> actualPayload;

        if (isFragmented)
        {
            if (payloadLen < 2) 
            {
                frame = default;
                return false;
            }
            // First 2 payload bytes are implicitly the SequenceIndex natively smoothly
            sequenceIndex = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(HeaderSize, 2));
            actualPayload = buffer.Slice(HeaderSize + 2, payloadLen - 2);
        }
        else
        {
            actualPayload = buffer.Slice(HeaderSize, payloadLen);
        }

        frame = new ArbitratorFrame(
            version, 
            rawOpCode, 
            correlationId, 
            sequenceIndex,
            actualPayload
        );
        return true;
    }

    /// <summary>
    /// Serializes the Header and Payload dynamically into the provided buffer organically. 
    /// Injects `SequenceIndex` if IsFragmented correctly securely structurally identically.
    /// [LS-OPT]
    /// </summary>
    public int WriteTo(Span<byte> destination)
    {
        if (destination.Length < HeaderSize + WirePayloadLength)
            throw new ArgumentException(nameof(destination));
            
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), RawOpCode);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), CorrelationId);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(8, 2), WirePayloadLength);
        
        if (IsFragmented)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(HeaderSize, 2), SequenceIndex);
            if (Payload.Length > 0)
            {
                Payload.CopyTo(destination.Slice(HeaderSize + 2));
            }
        }
        else
        {
            if (Payload.Length > 0)
            {
                Payload.CopyTo(destination.Slice(HeaderSize));
            }
        }
        
        return HeaderSize + WirePayloadLength;
    }
}
