using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ManagedSecurity.Discovery;

public class OnvifDiscovery
{
    private const string MulticastAddress = "239.255.255.250";
    private const int MulticastPort = 3702;

    public async Task<List<string>> ProbeAsync(int timeoutMs = 3000)
    {
        var endpoints = new List<string>();
        using var client = new UdpClient();
        client.EnableBroadcast = true;
        
        var ipEndpoint = new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort);
        
        string probeMessage = 
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<Envelope xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\" xmlns=\"http://www.w3.org/2003/05/soap-envelope\">" +
            "  <Header>" +
            "    <wsa:MessageID xmlns:wsa=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\">uuid:" + Guid.NewGuid() + "</wsa:MessageID>" +
            "    <wsa:To xmlns:wsa=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\">urn:schemas-xmlsoap-org:ws:2005:04:discovery</wsa:To>" +
            "    <wsa:Action xmlns:wsa=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</wsa:Action>" +
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
                if (response.Contains("XAddrs"))
                {
                    // In a real implementation, we would parse the XML for the <XAddrs> tag
                    // which contains the management URL (and thus the IP/Port).
                    endpoints.Add(result.RemoteEndPoint.Address.ToString());
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reached
        }

        return endpoints;
    }
}
