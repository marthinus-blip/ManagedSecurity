using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedSecurity.Orchestration.Arbitrator;

/// <summary>
/// A persistent outbound WebSocket manager designed to pierce NATs autonomously via WebSockets.
/// Guarantees connectivity explicitly.
/// </summary>
[ManagedSecurity.Common.Attributes.AllowMagicValues]
public class ArbitratorConnectorBehavior : IAgentBehavior
{
    public string Name => "ArbitratorConnector";
    
    private readonly string _agentId;
    private readonly string _arbitratorUrl;
    private bool _isRunning;

    public ArbitratorConnectorBehavior(string agentId, string arbitratorUrl)
    {
        _agentId = agentId;
        _arbitratorUrl = arbitratorUrl;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _isRunning = true;
        Console.WriteLine($"[CONNECTOR] Initiating persistent outbound tunnel. Target: {_arbitratorUrl}");
        
        while (!ct.IsCancellationRequested && _isRunning)
        {
            using var client = new ClientWebSocket();
            try
            {
                var uri = new Uri($"{_arbitratorUrl}?agentId={_agentId}");
                await client.ConnectAsync(uri, ct);
                Console.WriteLine($"[CONNECTOR] Tunnel established: {uri}");

                var buffer = new byte[1024];
                while (client.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    
                    // Future: Route P2P Signaling parameters to orchestrate WebRTC Handshake
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"[CONNECTOR] Tunnel disconnected: {ex.Message}. Reconnecting in 5s.");
            }
            
            await Task.Delay(5000, ct);
        }
    }

    public Task StopAsync()
    {
        _isRunning = false;
        return Task.CompletedTask;
    }
}
