using System;
using System.Buffers.Binary;
using System.Text;

namespace ManagedSecurity.Protocol;

/// <summary>
/// A zero-allocation, reflection-free forward-only parser for the 32-bit mapped protocol.
/// Operates identically to DbDataReader on the hot-path, providing absolute NativeAOT safety.
/// </summary>
public ref struct SentinelPayloadReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    /// <summary>
    /// Gets the schema identifier defined by the first 32 bits of the transmission.
    /// </summary>
    public uint SchemaId { get; }

    /// <summary>
    /// Evaluates the Most Significant Bit (MSB) to determine if this schema identifier 
    /// belongs to the Vendor Extension namespace (e.g. com_proj).
    /// </summary>
    public bool IsCustomSchema => (SchemaId & 0x80000000) != 0;

    public SentinelPayloadReader(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < sizeof(uint))
            throw new ArgumentOutOfRangeException(nameof(payload), "Payload is too short to contain a Schema ID");

        SchemaId = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        _buffer = payload;
        _position = sizeof(uint);
    }

    public int ReadInt32()
    {
        EnsureCapacity(sizeof(int));
        int value = BinaryPrimitives.ReadInt32LittleEndian(_buffer.Slice(_position));
        _position += sizeof(int);
        return value;
    }

    public uint ReadUInt32()
    {
        EnsureCapacity(sizeof(uint));
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Slice(_position));
        _position += sizeof(uint);
        return value;
    }

    public float ReadSingle()
    {
        EnsureCapacity(sizeof(float));
        float value = BinaryPrimitives.ReadSingleLittleEndian(_buffer.Slice(_position));
        _position += sizeof(float);
        return value;
    }

    public double ReadDouble()
    {
        EnsureCapacity(sizeof(double));
        double value = BinaryPrimitives.ReadDoubleLittleEndian(_buffer.Slice(_position));
        _position += sizeof(double);
        return value;
    }

    public bool ReadBoolean()
    {
        EnsureCapacity(sizeof(byte));
        bool value = _buffer[_position] != 0;
        _position += sizeof(byte);
        return value;
    }

    public ReadOnlySpan<byte> ReadBytes(int length)
    {
        EnsureCapacity(length);
        ReadOnlySpan<byte> value = _buffer.Slice(_position, length);
        _position += length;
        return value;
    }

    /// <summary>
    /// Reads a UTF-8 string prefixed by a 32-bit length integer.
    /// This is the only function that allocates a managed string object, isolating GC pressure.
    /// </summary>
    public string ReadString()
    {
        int stringLength = ReadInt32();
        if (stringLength == 0) return string.Empty;

        EnsureCapacity(stringLength);
        string value = Encoding.UTF8.GetString(_buffer.Slice(_position, stringLength));
        _position += stringLength;
        return value;
    }

    /// <summary>
    /// Reads the raw character span without allocating a managed String object.
    /// </summary>
    public int ReadStringSpan(Span<char> destination)
    {
        int stringLength = ReadInt32();
        if (stringLength == 0) return 0;

        EnsureCapacity(stringLength);
        int charsWritten = Encoding.UTF8.GetChars(_buffer.Slice(_position, stringLength), destination);
        _position += stringLength;
        return charsWritten;
    }

    private void EnsureCapacity(int bytesRequired)
    {
        if (_position + bytesRequired > _buffer.Length)
            throw new InvalidOperationException($"Buffer overrun. Expected {bytesRequired} bytes but only {_buffer.Length - _position} available.");
    }
}
