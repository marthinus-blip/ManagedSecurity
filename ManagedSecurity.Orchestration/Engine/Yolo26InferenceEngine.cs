using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using ManagedSecurity.Common.Logging;

namespace ManagedSecurity.Orchestration.Engine;

/// <summary>
/// Simulated implementation of YOLO26 GPL-3.0 Engine boundary.
/// The GPL-3.0 code is completely isolated here and called via P/Invoke
/// without contaminating the Sentinel Dashboard which relies solely on standard HTTP.
/// </summary>
public sealed partial class Yolo26InferenceEngine : IYoloInferenceEngine
{
    private readonly OrchestrationConfig _config;
    private bool _hasNativeLibrary;

    public bool IsNative => _hasNativeLibrary;

    public Yolo26InferenceEngine(OrchestrationConfig config)
    {
        _config = config;
        
        // [thought_yolo26_init]((2026-03-15T10:13:28) (Why: Initialize neural weights on start to eliminate hotpath spikes))
        // We elegantly probe for the native library to prevent throwing exceptions on the inference hotpath.
        if (NativeLibrary.TryLoad("sentinel_yolo26_core", typeof(Yolo26InferenceEngine).Assembly, DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.SafeDirectories, out var _))
        {
            _hasNativeLibrary = true;
            SentinelLogger.Heartbeat(SentinelLogger.CreateLogger<Yolo26InferenceEngine>(), "Yolo26InferenceEngine", "Initialized weights (GPL-3.0 Native Mode Attached)");
        }
        else
        {
            _hasNativeLibrary = false;
            SentinelLogger.NoSignal(SentinelLogger.CreateLogger<Yolo26InferenceEngine>(), "Yolo26InferenceEngine", "libsentinel_yolo26_core.so not found! Edge deployment falling back to Telemetry Simulation Mode.");
        }
    }

    [LibraryImport("sentinel_yolo26_core", EntryPoint = "Yolo26_Detect_Tensor")]
    private static partial int Yolo26_Detect_Tensor(
        ref byte tensorData, 
        int length, 
        float threshold, 
        [Out] YoloBoundingBox[] detections, 
        int maxDetections);

    public async Task<YoloBoundingBox[]> DetectAsync(IVisualTensor frame, CancellationToken ct)
    {
        // 1. Synchronously execute the Native AOT P/Invoke barrier
        // Must extract span logic to synchronous non-async method to satisfy CS4012.
        var detections = CheckTensorAndInvokeSync(frame, _config.YoloConfidenceThreshold);
        
        // 2. Wrap via Task.Yield or Task.Run if the native library blocks heavily. 
        // For YOLO inference latency hiding, we offload to the thread pool for demonstration.
        // Ensure ground truth representation of asynchronous delays.
        await Task.Yield(); 
        
        return detections;
    }

    private YoloBoundingBox[] CheckTensorAndInvokeSync(IVisualTensor frame, float threshold)
    {
        // Zero-Allocation Hot Path rule enforced via ReadOnlySpan<byte>.
        // We obtain the pointer for unmanaged interop natively without copying the buffer out of the stream memory.
        ReadOnlySpan<byte> tensorData = frame.Data;
        if (tensorData.IsEmpty) return Array.Empty<YoloBoundingBox>();

        // Pre-allocate buffer for maximum detections to prevent re-allocations
        // In a production tight loop, this would be an ArrayPool<YoloBoundingBox> to truly be zero-allocation.
        var buffer = new YoloBoundingBox[64];
        
        if (!_hasNativeLibrary)
        {
            // [thought_fallback_simulation]((2026-03-15T11:00:00) (Simulating inference when the GPL library is absent so UI work can proceed))
            return SimulateInference(threshold);
        }

        int hits = 0;
        try 
        {
            // Use MemoryMarshal to get a reference to the span's backing memory cleanly
            // and securely without invoking unsafe 'fixed' pins if possible, although 
            // the LibraryImport generator handles the pinning for ref parameters.
            hits = Yolo26_Detect_Tensor(
                ref MemoryMarshal.GetReference(tensorData), 
                tensorData.Length, 
                threshold, 
                buffer, 
                buffer.Length);
        }
        catch (DllNotFoundException)
        {
            _hasNativeLibrary = false;
            return SimulateInference(threshold);
        }

        if (hits <= 0) return Array.Empty<YoloBoundingBox>();
        if (hits > buffer.Length) hits = buffer.Length;

        var result = new YoloBoundingBox[hits];
        Array.Copy(buffer, result, hits);
        return result;
    }

    private YoloBoundingBox[] SimulateInference(float threshold)
    {
        // Simulate returning high-confidence pedestrian/vehicle targets
        var target1 = new YoloBoundingBox(
            x: 0.5f, y: 0.5f, width: 0.2f, height: 0.6f, 
            confidence: threshold + 0.15f, classId: 0 // Pedestrian
        );

        return new[] { target1 };
    }

    public void Dispose()
    {
        SentinelLogger.Heartbeat(SentinelLogger.CreateLogger<Yolo26InferenceEngine>(), "Yolo26InferenceEngine", "Neural weights unloaded.");
    }
}
