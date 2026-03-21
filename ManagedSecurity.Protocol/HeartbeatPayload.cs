using MemoryPack;

namespace ManagedSecurity.Protocol;

[MemoryPackable]
public partial struct HeartbeatPayload
{
    public int CpuLoad { get; set; }
    public float MemoryUsage { get; set; }
    public string Status { get; set; }
}
