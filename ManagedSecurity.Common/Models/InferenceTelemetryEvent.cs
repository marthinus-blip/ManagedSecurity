using System;

namespace ManagedSecurity.Common.Models;

public record BoundingBox
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public float Confidence { get; init; }
    public int ClassId { get; init; }
    public string Label { get; init; } = "unknown";
}

public record InferenceTelemetryEvent
{
    public string CameraId { get; init; } = string.Empty;
    public ulong TimestampMs { get; init; }
    public BoundingBox[] Detections { get; init; } = Array.Empty<BoundingBox>();
    public bool IsNative { get; init; }
}
