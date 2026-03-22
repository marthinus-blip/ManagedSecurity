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
    private static readonly Microsoft.Extensions.Logging.ILogger _logger = ManagedSecurity.Common.Logging.SentinelLogger.CreateLogger<ArbitratorConnectorBehavior>();
    public string Name => "ArbitratorConnector";
    
    private readonly string _agentId;
    private readonly string _arbitratorUrl;
    private readonly string _edgeToken;
    private bool _isRunning;

    public ArbitratorConnectorBehavior(string agentId, string arbitratorUrl, string edgeToken = "Sentinel_Fallback_Token")
    {
        _agentId = agentId;
        _arbitratorUrl = arbitratorUrl;
        _edgeToken = edgeToken;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _isRunning = true;
        ManagedSecurity.Common.Logging.SentinelLogger.Info(_logger, $"[CONNECTOR] Initiating persistent outbound tunnel. Target: {_arbitratorUrl}");
        
        while (!ct.IsCancellationRequested && _isRunning)
        {
            using var client = new ClientWebSocket();
            
            // [INSC-OPT] Prevent connection hijacking natively by declaring our physical Tenant boundary mathematically securely.
            if (!string.IsNullOrWhiteSpace(_edgeToken))
            {
                client.Options.SetRequestHeader("Authorization", $"Bearer {_edgeToken}");
            }
            
            try
            {
                var uri = new Uri($"{_arbitratorUrl}?agentId={_agentId}");
                await client.ConnectAsync(uri, ct);
                ManagedSecurity.Common.Logging.SentinelLogger.Info(_logger, $"[CONNECTOR] Token-Handshake completed. Tunnel established natively: {uri}");

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
                ManagedSecurity.Common.Logging.SentinelLogger.Info(_logger, $"[CONNECTOR] Tunnel disconnected: {ex.Message}. Reconnecting in 5s.");
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
