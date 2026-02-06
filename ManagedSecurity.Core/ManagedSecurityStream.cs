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
    private int _chunkSize;
    private byte[]? _metadata;
    private ulong _seekTableOffset;

    private byte[] _buffer;
    private int _bufferPos;
    private ulong _sequenceNumber;
    private bool _isHeaderProcessed;
    private bool _isFinalBlockProcessed;

    private readonly bool _leaveOpen;
    private readonly List<SeekPoint> _seekPoints = new();
    private readonly DateTimeOffset _streamStartTime = DateTimeOffset.UtcNow;

    // Telemetry
    public long TotalBytesProcessed { get; private set; }
    public int FrameCount { get; private set; }
    public double LastFrameDurationMs { get; private set; }
    public double ThroughputMbps { get; private set; }
    private readonly System.Diagnostics.Stopwatch _stopwatch = new();


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
        byte[]? metadata = null,
        bool leaveOpen = false,
        bool skipMasterHeader = false,
        ulong initialSequence = 0,
        ulong seekTableOffset = 0)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
        _mode = mode;
        _keyIndex = keyIndex;
        _highSecurity = highSecurity;
        _chunkSize = chunkSize;
        _metadata = metadata;
        _leaveOpen = leaveOpen;
        _isHeaderProcessed = skipMasterHeader;
        _sequenceNumber = initialSequence;
        _seekTableOffset = seekTableOffset;

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
    public void FlushToFrame(uint? timestampMs = null)
    {
        if (_mode != ManagedSecurityStreamMode.Encrypt)
            throw new InvalidOperationException("FlushToFrame is only supported in Encrypt mode.");
            
        // If it's a seekable stream, we record the offset of this new frame start
        if (_innerStream.CanSeek)
        {
            uint ts = timestampMs ?? (uint)(DateTimeOffset.UtcNow - _streamStartTime).TotalMilliseconds;
            _seekPoints.Add(new SeekPoint(ts, (ulong)_innerStream.Position, (uint)_sequenceNumber));
        }

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
            
            const int ProfileId = 2;
            int baseSize = _cipher.GetRequiredSize(_chunkSize, _keyIndex, ProfileId) - _chunkSize;
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

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_mode != ManagedSecurityStreamMode.Encrypt)
            throw new InvalidOperationException("Stream is not in Encrypt mode.");

        if (!_isHeaderProcessed)
        {
            WriteProtocolHeader(); // Header is small, sync is fine
            _isHeaderProcessed = true;
        }

        int remaining = buffer.Length;
        int bufferOffset = 0;

        while (remaining > 0)
        {
            int spaceInBuffer = _chunkSize - _bufferPos;
            int toCopy = Math.Min(remaining, spaceInBuffer);
            
            const int ProfileId = 2;
            int baseSize = _cipher.GetRequiredSize(_chunkSize, _keyIndex, ProfileId) - _chunkSize;
            buffer.Slice(bufferOffset, toCopy).Span.CopyTo(_buffer.AsSpan(baseSize + _bufferPos));

            _bufferPos += toCopy;
            bufferOffset += toCopy;
            remaining -= toCopy;

            if (_bufferPos == _chunkSize)
            {
                await ProcessCurrentBlockAsync(isFinal: false);
            }
        }
    }

    public async Task FlushToFrameAsync(uint? timestampMs = null)
    {
        if (_mode != ManagedSecurityStreamMode.Encrypt)
            throw new InvalidOperationException("FlushToFrame is only supported in Encrypt mode.");
            
        if (_innerStream.CanSeek)
        {
            uint ts = timestampMs ?? (uint)(DateTimeOffset.UtcNow - _streamStartTime).TotalMilliseconds;
            _seekPoints.Add(new SeekPoint(ts, (ulong)_innerStream.Position, (uint)_sequenceNumber));
        }

        await ProcessCurrentBlockAsync(isFinal: false);
    }

    private async Task ProcessCurrentBlockAsync(bool isFinal)
    {
        if (_bufferPos == 0 && !isFinal) return;

        const int ProfileId = 2;
        int baseHeaderSize = _cipher.GetRequiredSize(_chunkSize, _keyIndex, ProfileId) - _chunkSize;
        
        _stopwatch.Restart();

        if (_cipher.AsyncEncryptor != null)
        {
            var key = _cipher.GetKey(_keyIndex);
            await _cipher.AsyncEncryptor.EncryptS2Async(_buffer.AsMemory(baseHeaderSize, _bufferPos), _buffer.AsMemory(0, _buffer.Length), _keyIndex, _sequenceNumber, key);
        }
        else
        {
            EncryptFrameSync(baseHeaderSize, ProfileId);
        }
        
        _stopwatch.Stop();
        
        UpdateTelemetryForEncryption(ProfileId);

        int packetSize = _cipher.GetRequiredSize(_bufferPos, _keyIndex, ProfileId);
        await _innerStream.WriteAsync(_buffer.AsMemory(0, packetSize));
        
        _bufferPos = 0;
        _sequenceNumber++;
    }

    private void EncryptFrameSync(int baseHeaderSize, int profileId)
    {
        Span<byte> plaintext = _buffer.AsSpan(baseHeaderSize, _bufferPos);
        _cipher.Encrypt(plaintext, _buffer, _keyIndex, profileId, _sequenceNumber);
    }

    private void UpdateTelemetryForEncryption(int profileId)
    {
        LastFrameDurationMs = _stopwatch.Elapsed.TotalMilliseconds;
        TotalBytesProcessed += _bufferPos;
        FrameCount++;
        
        double totalSeconds = (DateTimeOffset.UtcNow - _streamStartTime).TotalSeconds;
        if (totalSeconds > 0)
        {
            ThroughputMbps = (TotalBytesProcessed * 8.0) / (totalSeconds * 1000 * 1000);
        }
    }

    private void WriteProtocolHeader(ulong seekTableOffset = 0)
    {
        ushort metaLen = (ushort)(_metadata?.Length ?? 0);
        Span<byte> master = stackalloc byte[MasterHeader.FixedSize];
        MasterHeader.Write(master, StreamVersion, _chunkSize, _keyIndex, metaLen, seekTableOffset);
        
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
        int baseHeaderSize = _cipher.GetRequiredSize(_chunkSize, _keyIndex, ProfileId) - _chunkSize;
        
        _stopwatch.Restart();
        EncryptFrameSync(baseHeaderSize, ProfileId);
        _stopwatch.Stop();
        
        UpdateTelemetryForEncryption(ProfileId);

        int packetSize = _cipher.GetRequiredSize(_bufferPos, _keyIndex, ProfileId);
        _innerStream.Write(_buffer, 0, packetSize);
        
        _bufferPos = 0;
        _sequenceNumber++;
    }



    public override async ValueTask DisposeAsync()
    {
        if (_mode == ManagedSecurityStreamMode.Encrypt && !_isFinalBlockProcessed)
        {
            await ProcessCurrentBlockAsync(isFinal: true);
            _isFinalBlockProcessed = true;

            if (_innerStream.CanSeek && _seekPoints.Count > 0)
            {
                // Write Seek Table at the end
                ulong seekOffset = (ulong)_innerStream.Position;
                byte[] seekData = SeekTableSerializer.Serialize(_seekPoints);
                await _innerStream.WriteAsync(seekData);

                // Update Master Header
                _innerStream.Position = 0;
                WriteProtocolHeader(seekOffset);
                _innerStream.Position = (long)(seekOffset + (ulong)seekData.Length);
            }

            await _innerStream.FlushAsync();
        }
        if (!_leaveOpen)
        {
            await _innerStream.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_mode == ManagedSecurityStreamMode.Encrypt && !_isFinalBlockProcessed)
            {
                if (_cipher.AsyncEncryptor != null)
                {
                    // If we have an async encryptor, we SHOULD have used DisposeAsync.
                    // However, to avoid a crash during a crash, we just try to process sync if we can,
                    // but we know it might fail on WASM.
                }

                ProcessCurrentBlock(isFinal: true);
                _isFinalBlockProcessed = true;

                if (_innerStream.CanSeek && _seekPoints.Count > 0)
                {
                    // Write Seek Table at the end
                    ulong seekOffset = (ulong)_innerStream.Position;
                    byte[] seekData = SeekTableSerializer.Serialize(_seekPoints);
                    _innerStream.Write(seekData);

                    // Update Master Header
                    _innerStream.Position = 0;
                    WriteProtocolHeader(seekOffset);
                    _innerStream.Position = (long)(seekOffset + (ulong)seekData.Length);
                }

                _innerStream.Flush();
            }
            if (!_leaveOpen)
            {
                _innerStream.Dispose();
            }
        }
        base.Dispose(disposing);
    }


    private int _readBufferOffset;
    private int _readBufferCount;

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_mode != ManagedSecurityStreamMode.Decrypt)
            throw new InvalidOperationException("Stream is not in Decrypt mode.");

        if (!_isHeaderProcessed)
        {
            await ReadProtocolHeaderAsync(cancellationToken);
            _isHeaderProcessed = true;
        }

        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            if (_readBufferCount > 0)
            {
                int toCopy = Math.Min(_readBufferCount, buffer.Length - totalRead);
                _buffer.AsMemory(_readBufferOffset, toCopy).CopyTo(buffer.Slice(totalRead));
                _readBufferOffset += toCopy;
                _readBufferCount -= toCopy;
                totalRead += toCopy;
                continue;
            }

            if (!await TryReadNextFrameAsync(cancellationToken))
            {
                break;
            }
        }

        return totalRead;
    }

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

    private async Task ReadProtocolHeaderAsync(CancellationToken ct)
    {
        byte[] masterData = new byte[MasterHeader.FixedSize];
        await ReadAllAsync(_innerStream, masterData, ct);

        // Transition to sync kernel for ref-struct handling
        ProcessMasterHeaderSync(masterData);

        if (_metadata != null && _metadata.Length > 0)
        {
            await ReadAllAsync(_innerStream, _metadata, ct);
        }
    }

    private void ProcessMasterHeaderSync(ReadOnlySpan<byte> data)
    {
        var master = new MasterHeader(data);
        _seekTableOffset = master.SeekTableOffset;
        
        if (master.Version != StreamVersion)
            throw new NotSupportedException($"Unsupported stream version: {master.Version}");

        // Dynamic Resize: Ensure _buffer can handle the chunkSize defined in the header
        if (master.ChunkSize > _chunkSize || _buffer.Length < master.ChunkSize)
        {
            // Profile ID 2 (Streaming) is standard for this stream implementation
            const int ProfileId = 2;
            int overhead = _cipher.GetRequiredSize(master.ChunkSize, master.KeyIndex, ProfileId) - master.ChunkSize;
            
            // Re-allocate internal buffer to accommodate larger frames
            var newBuffer = new byte[overhead + master.ChunkSize];
            
            // If we already had data in the buffer (partially read header), we'd need to copy it.
            // But since this happens before TryReadNextFrame, we can just swap.
            _buffer = newBuffer;
            _chunkSize = master.ChunkSize;
        }

        if (master.MetadataLength > 0)
        {
            _metadata = new byte[master.MetadataLength];
        }
    }

    private async Task<bool> TryReadNextFrameAsync(CancellationToken ct)
    {
        if (_seekTableOffset > 0 && _innerStream.CanSeek && (ulong)_innerStream.Position >= _seekTableOffset)
        {
            return false;
        }

        // 1. Read the fixed 4-byte header
        int r = await _innerStream.ReadAsync(_buffer.AsMemory(0, 4), ct);
        if (r == 0) return false;
        if (r < 4) 
        {
            // Try to fill the rest of the 4 bytes
            int remaining = 4 - r;
            while (remaining > 0)
            {
                int r2 = await _innerStream.ReadAsync(_buffer.AsMemory(4 - remaining, remaining), ct);
                if (r2 == 0) throw new EndOfStreamException("Frame header truncated.");
                remaining -= r2;
            }
        }

        // 2. Identify extensions and read them
        uint hVal = (uint)((_buffer[0] << 24) | (_buffer[1] << 16) | (_buffer[2] << 8) | _buffer[3]);
        int totalHeaderExt = CalculateHeaderExtensions(hVal);

        if (totalHeaderExt > 0)
        {
            await ReadAllAsync(_innerStream, _buffer.AsMemory(4, totalHeaderExt), ct);
        }

        // 3. Parse header and read payload
        var h = new Bindings.Header(_buffer.AsSpan(0, 4 + totalHeaderExt));
        int headerFullSize = 4 + totalHeaderExt;
        int remainingToRead = h.TotalLength - headerFullSize;

        await ReadAllAsync(_innerStream, _buffer.AsMemory(headerFullSize, remainingToRead), ct);

        // 4. Decrypt in Sync Kernel
        await DecryptCurrentFrameAsync(h);

        return true;
    }

    private async Task DecryptCurrentFrameAsync(Bindings.Header h)
    {
        int headerSize = h.TotalLength - h.PayloadLength;
        _stopwatch.Restart();

        if (_cipher.AsyncDecryptor != null && h.IvLength == 12 && h.SequenceLength == 8) // Profile S=2 check
        {
            var key = _cipher.GetKey(h.KeyIndex);
            await _cipher.AsyncDecryptor.DecryptS2Async(h, _buffer.AsMemory(0, h.TotalLength), _buffer.AsMemory(headerSize, h.PayloadLength), key);
        }
        else
        {
            DecryptFrameSync(h);
        }
        
        _stopwatch.Stop();
        ValidateFrameAndSetBuffer(h);
    }

    private void DecryptFrameSync(Bindings.Header h)
    {
        int headerSize = h.TotalLength - h.PayloadLength;
        _cipher.Decrypt(_buffer.AsSpan(0, h.TotalLength), _buffer.AsSpan(headerSize));
    }

    private void ValidateFrameAndSetBuffer(Bindings.Header h)
    {
        int headerSize = h.TotalLength - h.PayloadLength;
        LastFrameDurationMs = _stopwatch.Elapsed.TotalMilliseconds;
        TotalBytesProcessed += h.PayloadLength;
        FrameCount++;

        ulong frameSeq = BinaryPrimitives.ReadUInt64BigEndian(h.GetSequence(_buffer));
        if (frameSeq != _sequenceNumber)
            throw new CryptographicException($"Stream Sequence Mismatch! Expected {_sequenceNumber}, got {frameSeq}.");

        _readBufferOffset = headerSize;
        _readBufferCount = h.PayloadLength;
        _sequenceNumber++;
    }

    private static int CalculateHeaderExtensions(uint hVal)
    {
        ushort lRaw = (ushort)((hVal >> 12) & 0xFFF);
        int lExt = CountLeadingOnes12(lRaw);
        ushort kiRaw = (ushort)(hVal & 0xFFF);
        int kiExt = CountLeadingOnes12(kiRaw);
        return lExt + kiExt;
    }

    private async Task ReadAllAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        int totalRead = 0;
        int count = buffer.Length;
        while (totalRead < count)
        {
            int r = await stream.ReadAsync(buffer.Slice(totalRead), ct);
            if (r == 0) throw new EndOfStreamException("Stream ended before all data was read.");
            totalRead += r;
        }
    }

    public byte[]? Metadata => _metadata;

    private void ReadProtocolHeader()
    {
        Span<byte> masterData = stackalloc byte[MasterHeader.FixedSize];
        int read = 0;
        while (read < MasterHeader.FixedSize)
        {
            int r = _innerStream.Read(masterData.Slice(read));
            if (r == 0) throw new EndOfStreamException("Master header truncated.");
            read += r;
        }

        var master = new MasterHeader(masterData);
        _seekTableOffset = master.SeekTableOffset;
        _chunkSize = master.ChunkSize;
        
        if (master.Version != StreamVersion)
            throw new NotSupportedException($"Unsupported stream version: {master.Version}");

        if (master.MetadataLength > 0)
        {
            byte[] meta = new byte[master.MetadataLength];
            int metaRead = 0;
            while (metaRead < master.MetadataLength)
            {
                int r = _innerStream.Read(meta, metaRead, master.MetadataLength - metaRead);
                if (r == 0) throw new EndOfStreamException("Metadata truncated.");
                metaRead += r;
            }
            _metadata = meta;
        }
    }

    private bool TryReadNextFrame()
    {
        if (_seekTableOffset > 0 && _innerStream.CanSeek && (ulong)_innerStream.Position >= _seekTableOffset)
        {
            return false;
        }

        Span<byte> fixedH = _buffer.AsSpan(0, 4);
        int read = 0;
        while (read < 4)
        {
            int r = _innerStream.Read(fixedH.Slice(read));
            if (read == 0 && r == 0) return false;
            if (r == 0) throw new EndOfStreamException("Frame header truncated.");
            read += r;
        }

        uint hVal = (uint)((fixedH[0] << 24) | (fixedH[1] << 16) | (fixedH[2] << 8) | fixedH[3]);
        int totalHeaderExt = CalculateHeaderExtensions(hVal);

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

        var h = new Bindings.Header(_buffer);
        int headerFullSize = 4 + totalHeaderExt;
        int remainingToRead = h.TotalLength - headerFullSize;
        int dataRead = 0;
        while (dataRead < remainingToRead)
        {
            int r = _innerStream.Read(_buffer.AsSpan(headerFullSize + dataRead, remainingToRead - dataRead));
            if (r == 0) throw new EndOfStreamException("Frame data truncated.");
            dataRead += r;
        }

        int headerSize = h.TotalLength - h.PayloadLength;
        _stopwatch.Restart();
        _cipher.Decrypt(_buffer.AsSpan(0, h.TotalLength), _buffer.AsSpan(headerSize));
        _stopwatch.Stop();

        ValidateFrameAndSetBuffer(h);
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
