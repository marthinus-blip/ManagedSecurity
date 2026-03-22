using System;

namespace ManagedSecurity.Protocol;

/// <summary>
/// Constructs binary protocols efficiently chunking Massive Data arrays bypassing native 64KB limitations seamlessly physically optimally.
/// [LS-OPT] [FF-OPT]
/// </summary>
public static class ArbitratorFramer
{
    // Reserving 2 Bytes if fragmented, plus safe headroom. Max boundary = 65535, we use 60000 for network safety.
    public const int MaxChunkSize = 60000; 

    public delegate void FrameEmitter(ArbitratorFrame frame);

    /// <summary>
    /// Executes sequential payload segmentation cleanly allocating zero overhead tracking sequence indexes precisely automatically.
    /// Exceeds 64KB transparently dynamically effectively intuitively.
    /// </summary>
    public static void EmitFragments(ushort baseOpCode, uint correlationId, ReadOnlyMemory<byte> payload, FrameEmitter emitter)
    {
        if (payload.Length <= MaxChunkSize)
        {
            // [NSLD-OPT] Direct emission. Zero fragmentation overhead.
            emitter(new ArbitratorFrame(1, baseOpCode, correlationId, 0, payload.Span));
            return;
        }

        int totalChunks = (int)Math.Ceiling(payload.Length / (double)MaxChunkSize);
        ushort rawOpCodeFragment = (ushort)(baseOpCode | ArbitratorFrame.FragmentMask);
        ushort rawOpCodeFinal = (ushort)(rawOpCodeFragment | ArbitratorFrame.FragmentFinalMask);

        for (int i = 0; i < totalChunks; i++)
        {
            int offset = i * MaxChunkSize;
            int size = Math.Min(MaxChunkSize, payload.Length - offset);
            ReadOnlySpan<byte> slice = payload.Span.Slice(offset, size);
            
            ushort currentOpCode = (i == totalChunks - 1) ? rawOpCodeFinal : rawOpCodeFragment;
            
            // Generate the strictly mapped frame 
            var frame = new ArbitratorFrame(1, currentOpCode, correlationId, (ushort)i, slice);
            emitter(frame);
        }
    }
}
