using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedSecurity.Orchestration;

public class DomainBehavior : IAgentBehavior
{
    public string Name => "DomainDiscovery";
    private readonly string _agentId;
    private readonly string _version;
    private readonly string _role;
    private readonly int _discoveryPort = 5189;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;

    private readonly ConcurrentDictionary<string, PeerInfo> _peers = new();

    private readonly JsonSerializerOptions _options;
    public object? TypeInfo { get; set; } // For AOT

    public DomainBehavior(string agentId, string version, string role, JsonSerializerOptions? options = null)
    {
        _agentId = agentId;
        _version = version;
        _role = role;
        _options = options ?? new JsonSerializerOptions();
    }

    public record PeerInfo(string Id, string Version, string Role, DateTime LastSeen);

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        
        try 
        {
            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));

            // Start Loops
            _ = Task.Run(() => ListenLoop(_cts.Token), _cts.Token);
            _ = Task.Run(() => AnnounceLoop(_cts.Token), _cts.Token);
            
            Console.WriteLine($"[DOMAIN] Discovery active on port {_discoveryPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DOMAIN] Failed to start discovery: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient!.ReceiveAsync(ct);
                string json = Encoding.UTF8.GetString(result.Buffer);
                var peer = JsonSerializer.Deserialize<PeerAnnouncement>(json, _options);
                // Fallback attempt with reflection-free if needed, but the above should work if resolver is set.
                // However, the error suggests it's NOT working.
                
                if (peer != null && peer.Id != _agentId)
                {
                    var info = new PeerInfo(peer.Id, peer.Version, peer.Role, DateTime.UtcNow);
                    
                    if (!_peers.ContainsKey(peer.Id))
                    {
                         if (_peers.TryAdd(peer.Id, info))
                         {
                            Console.WriteLine($"[DOMAIN] New Peer Detected: {peer.Id} (v{peer.Version}) as {peer.Role}");
                            CompareAndNotify(info);
                         }
                    }
                    else 
                    {
                        _peers[info.Id] = info;
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) { /* Malformed packet */ }
        }
    }

    private async Task AnnounceLoop(CancellationToken ct)
    {
        var endpoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
        var announcement = new PeerAnnouncement(_agentId, _version, _role);
        byte[] data;
        // This is the CRITICAL part for AOT: Use the resolver if available
        if (_options.TypeInfoResolver != null)
        {
            data = JsonSerializer.SerializeToUtf8Bytes(announcement, _options);
        }
        else 
        {
             data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(announcement));
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _udpClient!.SendAsync(data, data.Length, endpoint);
            }
            catch (Exception) { /* Network transient */ }
            await Task.Delay(5000, ct); 
        }
    }

    private void CompareAndNotify(PeerInfo other)
    {
        // Simple string comparison for versions works because of yyyyMMdd-HHmm suffix
        int cmp = string.Compare(_version, other.Version, StringComparison.Ordinal);
        
        if (cmp < 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[DOMAIN] Higher version detected ({other.Id}: {other.Version}). We are a LESSER agent.");
            Console.ResetColor();
        }
        else if (cmp > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[DOMAIN] Lower version detected ({other.Id}: {other.Version}). We are the LEADER.");
            Console.ResetColor();
        }
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _udpClient?.Close();
        return Task.CompletedTask;
    }

    public record PeerAnnouncement(string Id, string Version, string Role);
}
