using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ManagedSecurity.Core;
using ManagedSecurity.Discovery;

namespace ManagedSecurity.Orchestration.Engine;

/// <summary>
/// Implements the "Heavy" Branch of the Machine Vision Pipeline.
/// Taps directly into the E2EE ManagedSecurityStream to receive Zero-Copy frames 
/// as they are decrypted during live transmission or playback.
/// </summary>
[ManagedSecurity.Common.Attributes.AllowMagicValues]
public sealed class DecryptedStreamFeedStrategy : IMachineVisionFeedStrategy
{
    private readonly ManagedSecurityStream _sourceStream;
    // We use a small bounded channel to drop frames if inference falls behind (backpressure).
    private readonly Channel<byte[]> _frameChannel;

    public DecryptedStreamFeedStrategy(ManagedSecurityStream sourceStream)
    {
        _sourceStream = sourceStream ?? throw new ArgumentNullException(nameof(sourceStream));
        
        // Bounded channel to prevent memory explosion if YOLO is slower than framerate.
        // We drop oldest frames to ensure the Inquisitor always processes the "freshest" data.
        _frameChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2) 
        { 
            FullMode = BoundedChannelFullMode.DropOldest 
        });

        // Hook into the zero-copy pipeline
        _sourceStream.OnFrameDecrypted += HandleFrameDecrypted;
        
        // [thought_stream_feed_strategy]((2026-03-15T11:45:00) (Why: Provides the Heavy Branch for fully streaming decoupled targets without network requests.))
    }

    private void HandleFrameDecrypted(ReadOnlySpan<byte> frameData)
    {
        // Because the ManagedSecurityStream core buffer will be overwritten by the next frame,
        // we defensively copy into the pipeline. If the pipeline is over capacity, the oldest
        // frame is automatically evicted.
        _frameChannel.Writer.TryWrite(frameData.ToArray());
    }

    public async Task<IVisualTensor?> GetNextFrameAsync(CancellationToken ct)
    {
        try
        {
            var buffer = await _frameChannel.Reader.ReadAsync(ct);
            return new RawVisualTensor(buffer);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _sourceStream.OnFrameDecrypted -= HandleFrameDecrypted;
        _frameChannel.Writer.TryComplete();
    }
}

/// <summary>
/// Implementation of IVisualTensor that wraps a raw E2EE byte stream chunk.
/// </summary>
public sealed class RawVisualTensor : IVisualTensor
{
    private readonly byte[] _buffer;
    public ReadOnlySpan<byte> Data => _buffer.AsSpan();
    public string Format => "RAW_H264";
    
    public RawVisualTensor(byte[] buffer)
    {
        _buffer = buffer;
    }
    
    public void Dispose()
    {
        // Future optimization: return _buffer to ArrayPool<byte>.Shared
    }
}
