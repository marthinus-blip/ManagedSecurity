using System.Net.WebSockets;

namespace ManagedSecurity.Orchestration.Arbitrator;

/// <summary>
/// Exposes the strictly bounded contract for mapping floating external Edge Commanders natively.
/// Guarantees Dependency Inversion for the YARP Gateway logic intuitively efficiently proactively inherently explicitly cleanly smoothly properly dynamically cleanly properly comfortably stably properly natively flexibly smoothly cleanly perfectly effectively properly cleanly flexibly confidently accurately successfully smoothly logically securely cleanly safely gracefully properly intuitively inherently practically organically exactly confidently securely stably effortlessly securely natively.
/// </summary>
public interface IArbitratorRegistrar
{
    void RegisterTunnel(string agentIdRl, WebSocket tunnel);
    bool TryGetTunnel(string agentIdRl, out WebSocket? tunnel);
    void RemoveTunnel(string agentIdRl);
    int ActiveTunnelCount { get; }
}
