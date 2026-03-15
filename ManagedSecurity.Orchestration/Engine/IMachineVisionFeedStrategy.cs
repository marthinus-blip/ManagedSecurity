using System;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedSecurity.Orchestration.Engine;

/// <summary>
/// Represents a raw frame ready for Machine Vision execution.
/// </summary>
public interface IVisualTensor : IDisposable
{
    /// <summary>
    /// Provide a unified pointer to the underlying data buffer (e.g. RGB or JPEG format).
    /// For zero-copy efficiency, this points directly to the allocated memory.
    /// </summary>
    ReadOnlySpan<byte> Data { get; }
    
    /// <summary>
    /// Format identifier of the underlying bytes (e.g., "JPEG", "RGB", "BGR")
    /// </summary>
    string Format { get; }
}

/// <summary>
/// A formalized feed strategy abstraction that decouple the frequency and cost of MV ingestion 
/// from the logical processing rules themselves (Guardian vs Inquisitor).
/// </summary>
public interface IMachineVisionFeedStrategy : IDisposable
{
    /// <summary>
    /// Fetches the next available frame. This may execute as a background HTTP poll (Light) or 
    /// a continuous zero-copy memory pointer hook (Heavy).
    /// </summary>
    /// <returns>An IVisualTensor wrapping the buffer, or null if cancelled/failed.</returns>
    Task<IVisualTensor?> GetNextFrameAsync(CancellationToken ct);
}
