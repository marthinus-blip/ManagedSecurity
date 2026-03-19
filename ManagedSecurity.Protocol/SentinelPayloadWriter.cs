using System;
using System.Buffers.Binary;
using System.Text;

namespace ManagedSecurity.Protocol;

/// <summary>
/// A zero-allocation, reflection-free sequential writer for the 32-bit mapped protocol.
/// Designed to operate on rented ArrayPool<byte> buffers or unmanaged Memory chunks.
/// </summary>
public ref struct SentinelPayloadWriter
{
    private readonly Span<byte> _buffer;
    private int _position;

    /// <summary>
    /// Returns the exact slice of bytes actively written so far, ready for transport.
    /// </summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.Slice(0, _position);

    public SentinelPayloadWriter(Span<byte> destinationBuffer, uint schemaId)
    {
        if (destinationBuffer.Length < sizeof(uint))
            throw new ArgumentOutOfRangeException(nameof(destinationBuffer), "Buffer is too small to contain a Schema ID");

        BinaryPrimitives.WriteUInt32LittleEndian(destinationBuffer, schemaId);
        
        _buffer = destinationBuffer;
        _position = sizeof(uint); // Starts at index 4
    }

    public void Write(int value)
    {
        EnsureCapacity(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(_position), value);
        _position += sizeof(int);
    }

    public void Write(uint value)
    {
        EnsureCapacity(sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.Slice(_position), value);
        _position += sizeof(uint);
    }

    public void Write(float value)
    {
        EnsureCapacity(sizeof(float));
        BinaryPrimitives.WriteSingleLittleEndian(_buffer.Slice(_position), value);
        _position += sizeof(float);
    }

    public void Write(double value)
    {
        EnsureCapacity(sizeof(double));
        BinaryPrimitives.WriteDoubleLittleEndian(_buffer.Slice(_position), value);
        _position += sizeof(double);
    }

    public void Write(bool value)
    {
        EnsureCapacity(sizeof(byte));
        _buffer[_position] = (byte)(value ? 1 : 0);
        _position += sizeof(byte);
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(_buffer.Slice(_position));
        _position += value.Length;
    }

    /// <summary>
    /// Writes a UTF-8 string prefixed by a 32-bit length integer.
    /// Operates without allocating intermediate char arrays on the heap.
    /// </summary>
    public void WriteString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Write(0);
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        Write(byteCount); // 32-bit length prefix

        EnsureCapacity(byteCount);
        Encoding.UTF8.GetBytes(value, _buffer.Slice(_position, byteCount));
        _position += byteCount;
    }

    private void EnsureCapacity(int bytesRequired)
    {
        if (_position + bytesRequired > _buffer.Length)
            throw new InvalidOperationException($"Buffer overrun. Need {bytesRequired} more bytes, but only {_buffer.Length - _position} available.");
    }
}
