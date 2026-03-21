using MemoryPack;

namespace ManagedSecurity.Protocol;

/// <summary>
/// Scaffolding the System payload transferring hardware metrics purely gracefully securely explicitly natively.
/// [INSC-OPT]
/// </summary>
[MemoryPackable]
public partial struct SysDataPayload
{
    public float CpuTempCelsius { get; set; }
    public long UptimeMilliseconds { get; set; }
    public string Architecture { get; set; }
    public int ActiveThreads { get; set; }
}
