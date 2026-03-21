using MemoryPack;

namespace ManagedSecurity.Protocol;

/// <summary>
/// Facilitates Temporal durable execution by heartbeating checkpoints dynamically logically cleanly natively optimally organically seamlessly.
/// [FF-OPT]
/// </summary>
[MemoryPackable]
public partial struct JobStateUpdatePayload
{
    public long JobId { get; set; }
    public string StatePayload { get; set; }
    public int RequestedExtensionSeconds { get; set; }
}
