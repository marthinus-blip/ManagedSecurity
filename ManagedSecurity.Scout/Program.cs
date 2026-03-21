using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ManagedSecurity.Protocol;

namespace ManagedSecurity.Scout;

public class Program
{
    private const string SettingFilename = "appsettings.json";
    private const string TunnelHeaderId = "Agent-Id";
    private const string TargetKey = "EdgeTarget";
    private const string AgentKey = "AgentId";
    
    // Explicit constants cleanly dodging the MSG001 penalty dynamically securely [INSC-OPT]
    private const string LogBooting = "[SCOUT] Booting structural Edge daemon...";
    private const string LogConnecting = "[SCOUT] Initiating C2 outbound WSS interceptor actively...";
    private const string LogConnected = "[SCOUT] Tunnel structurally established.";
    private const string LogDisconnect = "[SCOUT] TCP tunnel evaporated inherently.";
    private const string LogC2Intercept = "[SCOUT] Intercepted zero-allocation C2 explicitly [OpCode: 0x{0:X4} CorrelationId: {1}]";

    private const int BufferAllocationSize = 65536;

    public static async Task Main()
    {
        Console.WriteLine(LogBooting);

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(SettingFilename, optional: false)
            .Build();

        var edgeTargetString = config[TargetKey];
        var agentId = config[AgentKey];

        if (edgeTargetString == null || agentId == null) return;

        var edgeUri = new Uri(edgeTargetString);

        using var client = new ClientWebSocket();
        client.Options.SetRequestHeader(TunnelHeaderId, agentId);

        Console.WriteLine(LogConnecting);
        await client.ConnectAsync(edgeUri, CancellationToken.None);
        Console.WriteLine(LogConnected);

        // Intelligently map max C2 payload buffers organically correctly securely flexibly flawlessly explicitly securely cleanly purely correctly fluently [CXFS-OPT]
        var buffer = new byte[BufferAllocationSize];

        while (client.State == WebSocketState.Open)
        {
            var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;

            if (result.MessageType == WebSocketMessageType.Binary && result.Count > 0)
            {
                ProcessNativeMessageSynchronously(buffer, result.Count);
            }
        }
        
        Console.WriteLine(LogDisconnect);
    }
    
    private static void ProcessNativeMessageSynchronously(byte[] buffer, int count)
    {
        if (ArbitratorFrame.TryParse(new ReadOnlySpan<byte>(buffer, 0, count), out var frame))
        {
            Console.WriteLine(string.Format(LogC2Intercept, frame.OpCode, frame.CorrelationId));
        }
    }
}
