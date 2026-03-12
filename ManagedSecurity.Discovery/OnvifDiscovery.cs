using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedSecurity.Discovery;

public record OnvifDevice(string IpAddress, string[] Scopes, string[] XAddrs)
{
    public string? Model => Scopes.FirstOrDefault(s => s.Contains("/hardware/"))?.Split('/').Last();
    public string? Name => Scopes.FirstOrDefault(s => s.Contains("/name/"))?.Split('/').Last().Replace("_", " ");
}

public class OnvifDiscovery
{
    private const string MulticastAddress = "239.255.255.250";
    private const int MulticastPort = 3702;

    public async Task<List<OnvifDevice>> ProbeAsync(int timeoutMs = 3000)
    {
        var devices = new List<OnvifDevice>();
        using var client = new UdpClient();
        client.EnableBroadcast = true;
        
        var ipEndpoint = new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort);
        
        string probeId = Guid.NewGuid().ToString();
        string probeMessage = 
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<Envelope xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\" xmlns=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:wsa=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\">" +
            "  <Header>" +
            "    <wsa:MessageID>uuid:" + probeId + "</wsa:MessageID>" +
            "    <wsa:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</wsa:To>" +
            "    <wsa:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</wsa:Action>" +
            "  </Header>" +
            "  <Body>" +
            "    <Probe xmlns=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\">" +
            "      <Types>dn:NetworkVideoTransmitter</Types>" +
            "    </Probe>" +
            "  </Body>" +
            "</Envelope>";

        byte[] data = Encoding.UTF8.GetBytes(probeMessage);
        await client.SendAsync(data, data.Length, ipEndpoint);

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var receiveTask = client.ReceiveAsync(cts.Token);
                var result = await receiveTask;
                
                string response = Encoding.UTF8.GetString(result.Buffer);
                if (response.Contains("ProbeMatches"))
                {
                    // Basic Regex parsing to avoid heavy XML dependencies in Core/Discovery
                    var scopesMatch = Regex.Match(response, "<(?:[^:]+:)?Scopes>([^<]+)</(?:[^:]+:)?Scopes>");
                    var xaddrsMatch = Regex.Match(response, "<(?:[^:]+:)?XAddrs>([^<]+)</(?:[^:]+:)?XAddrs>");

                    string[] scopes = scopesMatch.Success ? scopesMatch.Groups[1].Value.Split(' ') : Array.Empty<string>();
                    string[] xaddrs = xaddrsMatch.Success ? xaddrsMatch.Groups[1].Value.Split(' ') : Array.Empty<string>();

                    var device = new OnvifDevice(result.RemoteEndPoint.Address.ToString(), scopes, xaddrs);
                    if (!devices.Any(d => d.IpAddress == device.IpAddress))
                    {
                        devices.Add(device);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }

        return devices;
    }
}
