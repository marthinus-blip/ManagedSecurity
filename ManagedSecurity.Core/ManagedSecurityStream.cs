using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using ManagedSecurity.Common;

namespace ManagedSecurity.Core;

public enum ManagedSecurityStreamMode
{
    Encrypt,
    Decrypt
}

public class ManagedSecurityStream : Stream
{
    private readonly Stream _innerStream;
    private readonly Cipher _cipher;
    private readonly int _keyIndex;
    private readonly bool _highSecurity;
    private readonly ManagedSecurityStreamMode _mode;
    private readonly int _chunkSize;
    private byte[]? _metadata;

    private readonly byte[] _buffer;
    private int _bufferPos;
    private ulong _sequenceNumber;
    private bool _isHeaderProcessed;
    private bool _isFinalBlockProcessed;

    // Master Header Constants
    private static ReadOnlySpan<byte> StreamMagic => "MSG"u8;
    private const byte StreamVersion = 1;

    public ManagedSecurityStream(
        Stream innerStream, 
        Cipher cipher, 
        ManagedSecurityStreamMode mode,
        int keyIndex = 0, 
        bool highSecurity = false,
        int chunkSize = 64 * 1024,
        byte[]? metadata = null)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
        _mode = mode;
        _keyIndex = keyIndex;
        _highSecurity = highSecurity;
        _chunkSize = chunkSize;
        _metadata = metadata;

        // We use S=2 (Streaming Profile) which includes the 8-byte sequence number
        const int ProfileId = 2;
        // Pre-allocate buffer with enough space for MAX Header + Chunk
        int overhead = _cipher.GetRequiredSize(chunkSize, keyIndex, ProfileId) - chunkSize;
        _buffer = new byte[overhead + chunkSize];
    }

    public override bool CanRead => _mode == ManagedSecurityStreamMode.Decrypt;
    public override bool CanWrite => _mode == ManagedSecurityStreamMode.Encrypt;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() => _innerStream.Flush();

    /// <summary>
    /// Forcefully encrypts any data in the current buffer into a frame.
    /// Use this at critical media boundaries (e.g., H264 I-Frames).
    /// </summary>
    public void FlushToFrame()
    {
        if (_mode != ManagedSecurityStreamMode.Encrypt)
            throw new InvalidOperationException("FlushToFrame is only supported in Encrypt mode.");
            
        ProcessCurrentBlock(isFinal: false);
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_mode != ManagedSecurityStreamMode.Encrypt)
            throw new InvalidOperationException("Stream is not in Encrypt mode.");

        if (!_isHeaderProcessed)
        {
            WriteProtocolHeader();
            _isHeaderProcessed = true;
        }

        int remaining = buffer.Length;
        int bufferOffset = 0;

        while (remaining > 0)
        {
            int spaceInBuffer = _chunkSize - _bufferPos;
            int toCopy = Math.Min(remaining, spaceInBuffer);
            
            // Layout of _buffer: [Header Space] [Payload Space]
            const int ProfileId = 2;
            int baseSize = _cipher.GetRequiredSize(0, _keyIndex, ProfileId);
            buffer.Slice(bufferOffset, toCopy).CopyTo(_buffer.AsSpan(baseSize + _bufferPos));
            
            _bufferPos += toCopy;
            bufferOffset += toCopy;
            remaining -= toCopy;

            if (_bufferPos == _chunkSize)
            {
                ProcessCurrentBlock(isFinal: false);
            }
        }
    }

    private void WriteProtocolHeader()
    {
        Span<byte> master = stackalloc byte[14];
        StreamMagic.CopyTo(master);
        master[3] = StreamVersion;
        BinaryPrimitives.WriteInt32BigEndian(master.Slice(4), _chunkSize);
        BinaryPrimitives.WriteInt32BigEndian(master.Slice(8), _keyIndex);
        
        ushort metaLen = (ushort)(_metadata?.Length ?? 0);
        BinaryPrimitives.WriteUInt16BigEndian(master.Slice(12), metaLen);
        
        _innerStream.Write(master);
        if (metaLen > 0)
        {
            _innerStream.Write(_metadata!);
        }
    }

    private void ProcessCurrentBlock(bool isFinal)
    {
        if (_bufferPos == 0 && !isFinal) return;

        const int ProfileId = 2;
        // Use the MAX possible header size for this chunkSize to ensure consistent offsets
        int baseHeaderSize = _cipher.GetRequiredSize(_chunkSize, _keyIndex, ProfileId) - _chunkSize;
        Span<byte> plaintext = _buffer.AsSpan(baseHeaderSize, _bufferPos);
        
        // Encrypt with ProfileId=2 and our sequenceNumber
        _cipher.Encrypt(plaintext, _buffer, _keyIndex, ProfileId, _sequenceNumber);
        
        int packetSize = _cipher.GetRequiredSize(_bufferPos, _keyIndex, ProfileId);
        _innerStream.Write(_buffer, 0, packetSize);
        
        _bufferPos = 0;
        _sequenceNumber++;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_mode == ManagedSecurityStreamMode.Encrypt && !_isFinalBlockProcessed)
            {
                ProcessCurrentBlock(isFinal: true);
                _isFinalBlockProcessed = true;
                _innerStream.Flush();
            }
            _innerStream.Dispose();
        }
        base.Dispose(disposing);
    }

    private int _readBufferOffset;
    private int _readBufferCount;

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_mode != ManagedSecurityStreamMode.Decrypt)
            throw new InvalidOperationException("Stream is not in Decrypt mode.");

        if (!_isHeaderProcessed)
        {
            ReadProtocolHeader();
            _isHeaderProcessed = true;
        }

        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            // 1. If we have data in our internal buffer, serve it first
            if (_readBufferCount > 0)
            {
                int toCopy = Math.Min(_readBufferCount, buffer.Length - totalRead);
                
                _buffer.AsSpan(_readBufferOffset, toCopy)
                       .CopyTo(buffer.Slice(totalRead));
                
                _readBufferOffset += toCopy;
                _readBufferCount -= toCopy;
                totalRead += toCopy;
                continue;
            }

            // 2. Otherwise, read the next frame from the inner stream
            if (!TryReadNextFrame())
            {
                break; // End of stream
            }
        }

        return totalRead;
    }

    public byte[]? Metadata => _metadata;

    private void ReadProtocolHeader()
    {
        Span<byte> master = stackalloc byte[14];
        int read = 0;
        while (read < 14)
        {
            int r = _innerStream.Read(master.Slice(read));
            if (r == 0) throw new EndOfStreamException("Master header truncated.");
            read += r;
        }

        if (!master.Slice(0, 3).SequenceEqual(StreamMagic))
            throw new InvalidDataException("Invalid stream magic.");
        
        if (master[3] != StreamVersion)
            throw new NotSupportedException($"Unsupported stream version: {master[3]}");

        // Note: For now we assume the caller matches the chunk size or we use what's in the header.
        // In a real implementation, we'd adjust _chunkSize and re-allocate _buffer if needed.
        
        ushort metaLen = BinaryPrimitives.ReadUInt16BigEndian(master.Slice(12));
        if (metaLen > 0)
        {
            byte[] meta = new byte[metaLen];
            int metaRead = 0;
            while (metaRead < metaLen)
            {
                int r = _innerStream.Read(meta, metaRead, metaLen - metaRead);
                if (r == 0) throw new EndOfStreamException("Metadata truncated.");
                metaRead += r;
            }
            _metadata = meta;
        }
    }

    private bool TryReadNextFrame()
    {
        // 1. Read the Fixed Header (4 bytes)
        Span<byte> fixedH = _buffer.AsSpan(0, 4);
        int read = 0;
        while (read < 4)
        {
            int r = _innerStream.Read(fixedH.Slice(read));
            if (read == 0 && r == 0) return false; // Graceful EOF
            if (r == 0) throw new EndOfStreamException("Frame header truncated.");
            read += r;
        }

        // 2. Peek at extension bits to calculate how many bytes to read
        // Layout: [3 Magic][2 Ver][2 Switch][1 Res] [12 L] [12 KI]
        uint hVal = (uint)((fixedH[0] << 24) | (fixedH[1] << 16) | (fixedH[2] << 8) | fixedH[3]);
        
        // Profile/Switch (Bits 5-6)
        int s = (int)((hVal >> 25) & 0x03);
        int ivLen = (s == 1) ? 16 : 12;
        int macLen = (s == 1) ? 32 : 16;
        int seqLen = (s == 2) ? 8 : 0;

        // Count extensions for L (Top 12 bits of last 24)
        ushort lRaw = (ushort)((hVal >> 12) & 0xFFF);
        int lExt = CountLeadingOnes12(lRaw);
        
        // Count extensions for KI (Bottom 12 bits)
        ushort kiRaw = (ushort)(hVal & 0xFFF);
        int kiExt = CountLeadingOnes12(kiRaw);

        int totalHeaderExt = lExt + kiExt;
        
        // 3. Read Header Extensions
        if (totalHeaderExt > 0)
        {
            int extRead = 0;
            while (extRead < totalHeaderExt)
            {
                int r = _innerStream.Read(_buffer.AsSpan(4 + extRead, totalHeaderExt - extRead));
                if (r == 0) throw new EndOfStreamException("Header extensions truncated.");
                extRead += r;
            }
        }

        // 4. Now we can safely call Header constructor to get full lengths
        // We pass the full buffer to satisfy the internal 'TotalLength' check.
        var h = new Bindings.Header(_buffer);
        
        // 5. Read the rest of the frame (everything after header extensions)
        int headerFullSize = 4 + totalHeaderExt;
        int remainingToRead = h.TotalLength - headerFullSize;
        int dataRead = 0;
        while (dataRead < remainingToRead)
        {
            int r = _innerStream.Read(_buffer.AsSpan(headerFullSize + dataRead, remainingToRead - dataRead));
            if (r == 0) throw new EndOfStreamException("Frame data truncated.");
            dataRead += r;
        }

        // 6. Decrypt and Validate Sequence
        const int ProfileId = 2;
        
        // Use the actual header size of THIS frame
        int headerSize = h.TotalLength - h.PayloadLength;
        _cipher.Decrypt(_buffer.AsSpan(0, h.TotalLength), _buffer.AsSpan(headerSize));

        // 7. Check Sequence Number
        ulong frameSeq = BinaryPrimitives.ReadUInt64BigEndian(h.GetSequence(_buffer));
        if (frameSeq != _sequenceNumber)
        {
            throw new CryptographicException($"Stream Sequence Mismatch! Expected {_sequenceNumber}, got {frameSeq}. Possible frame swap attack.");
        }

        _readBufferOffset = headerSize;
        _readBufferCount = h.PayloadLength;
        _sequenceNumber++;
        return true;
    }

    private static int CountLeadingOnes12(ushort val)
    {
        int count = 0;
        ushort mask = 0x800; // Bit 11
        while ((val & mask) != 0)
        {
            count++;
            val <<= 1;
            if (count > 3) break;
        }
        return count;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
