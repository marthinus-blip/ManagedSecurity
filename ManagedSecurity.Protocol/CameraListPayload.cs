using MemoryPack;

namespace ManagedSecurity.Protocol;

/// <summary>
/// Handles Camera Discovery structures traversing seamlessly efficiently into the CV engines cleanly.
/// [INSC-OPT]
/// </summary>
[MemoryPackable]
public partial struct CameraListPayload
{
    public CameraEntry[] Cameras { get; set; }
}

[MemoryPackable]
public partial struct CameraEntry
{
    public string GlobalId { get; set; }
    public string StreamUrl { get; set; }
    public string SnapshotUrl { get; set; }
    public string Vendor { get; set; }
    
    /// <summary>
    /// Explicit mapping integer aligning structurally with MachineVisionRoute enum dynamically natively.
    /// </summary>
    public int RouteMechanism { get; set; }
}
