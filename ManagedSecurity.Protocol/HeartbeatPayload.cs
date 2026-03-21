using MemoryPack;

namespace ManagedSecurity.Protocol;

[MemoryPackable]
public partial struct HeartbeatPayload
{
    public uint SchemaId { get; set; }
    public int CpuLoad { get; set; }
    public float MemoryUsage { get; set; }
    public string Status { get; set; }
}
