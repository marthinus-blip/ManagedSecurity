using System;
using MemoryPack;

namespace ManagedSecurity.Protocol;

/// <summary>
/// A centralized reverse-routing object encapsulating signaling parameters efficiently bridging NAT contexts physically natively cleanly.
/// [LS-OPT] [FF-OPT]
/// </summary>
[MemoryPackable]
public partial class P2PSignalPayload
{
    public string TargetViewerId { get; set; } = string.Empty;
    public string SessionDescriptionProtocol { get; set; } = string.Empty;
    public string IceCandidate { get; set; } = string.Empty;
    public bool IsOffer { get; set; }
}
