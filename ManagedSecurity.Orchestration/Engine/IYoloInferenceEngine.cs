using System;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedSecurity.Orchestration.Engine;

/// <summary>
/// Defines a single object detection event from the Narrow Phase inference.
/// </summary>
public readonly struct YoloBoundingBox
{
    public readonly float X;
    public readonly float Y;
    public readonly float Width;
    public readonly float Height;
    public readonly float Confidence;
    public readonly int ClassId;

    public YoloBoundingBox(float x, float y, float width, float height, float confidence, int classId)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Confidence = confidence;
        ClassId = classId;
    }
}

/// <summary>
/// A formalized Machine Vision Inference abstraction for YOLO workloads.
/// Receives the zero-allocation visual tensor pipeline from the feed Strategy.
/// </summary>
public interface IYoloInferenceEngine : IDisposable
{
    bool IsNative { get; }
    string EngineVersion { get; }

    /// <summary>
    /// Executes native inference on the provided frame zero-copy.
    /// </summary>
    /// <param name="frame">The pre-processed tensor representation.</param>
    /// <param name="ct">Cancellation token for aborting inference early.</param>
    /// <returns>An array of detected objects above the confidence threshold.</returns>
    Task<YoloBoundingBox[]> DetectAsync(IVisualTensor frame, CancellationToken ct);
}
