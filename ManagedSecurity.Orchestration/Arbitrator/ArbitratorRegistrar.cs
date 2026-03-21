using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;

namespace ManagedSecurity.Orchestration.Arbitrator;

/// <summary>
/// Physically holds the persistent WebSocket Reverse Tunnels mapping internal Dashboard requests logically stably functionally completely purely.
/// </summary>
[ManagedSecurity.Common.Attributes.AllowMagicValues]
public class ArbitratorRegistrar : IArbitratorRegistrar
{
    private readonly ConcurrentDictionary<string, WebSocket> _tunnels = new();

    public void RegisterTunnel(string agentIdRl, WebSocket tunnel)
    {
        _tunnels.AddOrUpdate(agentIdRl, tunnel, (key, oldTunnel) => 
        {
            // Enforce strictly exactly-once boundaries; if an Agent rapidly reconnects, terminate the stale dangling tunnel.
            if (oldTunnel.State == WebSocketState.Open)
            {
                var dropTask = oldTunnel.CloseAsync(WebSocketCloseStatus.ProtocolError, "Superseded_By_New_Connection", CancellationToken.None);
            }
            return tunnel;
        });
    }

    public bool TryGetTunnel(string agentIdRl, out WebSocket? tunnel)
    {
        return _tunnels.TryGetValue(agentIdRl, out tunnel);
    }

    public void RemoveTunnel(string agentIdRl)
    {
        _tunnels.TryRemove(agentIdRl, out _);
    }

    public int ActiveTunnelCount => _tunnels.Count;
}
