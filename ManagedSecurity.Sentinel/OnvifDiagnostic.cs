using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace ManagedSecurity.Sentinel;

public static class OnvifDiagnostic
{
    public static async Task RunProbe()
    {
        const string MulticastAddress = "239.255.255.250";
        const int MulticastPort = 3702;

        using var client = new UdpClient();
        client.EnableBroadcast = true;
        
        var ipEndpoint = new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort);
        
        string probeId = Guid.NewGuid().ToString();
        string probeMessage = 
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<Envelope xmlns=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\" xmlns:wsa=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\">" +
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

        Console.WriteLine("[ONVIF] Sent probe...");

        using var cts = new CancellationTokenSource(5000);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(cts.Token);
                string response = Encoding.UTF8.GetString(result.Buffer);
                if (response.Contains("ProbeMatches"))
                {
                    Console.WriteLine($"\n[ONVIF] Match from {result.RemoteEndPoint.Address}:");
                    var xaddrsMatch = Regex.Match(response, "<(?:[^:]+:)?XAddrs>([^<]+)</(?:[^:]+:)?XAddrs>");
                    if (xaddrsMatch.Success)
                    {
                        Console.WriteLine($"[ONVIF] XAddrs: {xaddrsMatch.Groups[1].Value}");
                    }
                    var scopesMatch = Regex.Match(response, "<(?:[^:]+:)?Scopes>([^<]+)</(?:[^:]+:)?Scopes>");
                    if (scopesMatch.Success)
                    {
                        Console.WriteLine($"[ONVIF] Scopes: {scopesMatch.Groups[1].Value}");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public static async Task QueryStreamUri(string ip, string user, string pass)
    {
        string deviceServiceUrl = $"http://{ip}:80/onvif/device_service";
        Console.WriteLine($"[ONVIF] Querying {deviceServiceUrl}...");

        using var http = new System.Net.Http.HttpClient();

        // 1. Get Services to find Media service
        string getServices = @"<?xml version=""1.0"" encoding=""utf-8""?>
            <soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope"" xmlns:tds=""http://www.onvif.org/ver10/device/wsdl"">
              <soap:Body>
                <tds:GetServices><tds:IncludeCapability>false</tds:IncludeCapability></tds:GetServices>
              </soap:Body>
            </soap:Envelope>";

        var resp = await http.PostAsync(deviceServiceUrl, new StringContent(getServices, Encoding.UTF8, "application/soap+xml"));
        string content = await resp.Content.ReadAsStringAsync();

        var mediaServiceMatch = Regex.Match(content, @"<(?:[^:]+:)?Namespace>http://www.onvif.org/ver10/media/wsdl</(?:[^:]+:)?Namespace>\s*<(?:[^:]+:)?XAddr>([^<]+)</(?:[^:]+:)?XAddr>");
        if (!mediaServiceMatch.Success)
        {
             // Fallback for some cameras that only return the local part or use GetCapabilities
             mediaServiceMatch = Regex.Match(content, @"http://www.onvif.org/ver10/media/wsdl.*?XAddr>([^<]+)<", RegexOptions.Singleline);
        }

        if (!mediaServiceMatch.Success)
        {
            Console.WriteLine("[ONVIF] Media Service not found in GetServices. Trying GetCapabilities...");
            string getCapabilities = @"<?xml version=""1.0"" encoding=""utf-8""?>
                <soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope"" xmlns:tds=""http://www.onvif.org/ver10/device/wsdl"">
                  <soap:Body><tds:GetCapabilities><tds:Category>All</tds:Category></tds:GetCapabilities></soap:Body>
                </soap:Envelope>";
            resp = await http.PostAsync(deviceServiceUrl, new StringContent(getCapabilities, Encoding.UTF8, "application/soap+xml"));
            content = await resp.Content.ReadAsStringAsync();
            mediaServiceMatch = Regex.Match(content, @"<(?:[^:]+:)?Media>.*?<(?:[^:]+:)?XAddr>([^<]+)</(?:[^:]+:)?XAddr>.*?</(?:[^:]+:)?Media>", RegexOptions.Singleline);
        }

        if (!mediaServiceMatch.Success)
        {
            Console.WriteLine("[ONVIF] Failed to find Media Service XAddr.");
            return;
        }

        string mediaServiceUrl = mediaServiceMatch.Groups[1].Value;
        Console.WriteLine($"[ONVIF] Media Service URL: {mediaServiceUrl}");

        // 2. Get Profiles
        string getProfiles = @"<?xml version=""1.0"" encoding=""utf-8""?>
            <soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope"" xmlns:trt=""http://www.onvif.org/ver10/media/wsdl"">
              <soap:Body><trt:GetProfiles/></soap:Body>
            </soap:Envelope>";
        
        resp = await http.PostAsync(mediaServiceUrl, new StringContent(getProfiles, Encoding.UTF8, "application/soap+xml"));
        content = await resp.Content.ReadAsStringAsync();

        if (content.Contains("Unauthorized"))
        {
            Console.WriteLine("[ONVIF] Error: Media service returned Unauthorized. Dahua usually requires tokens.");
            return;
        }

        var profiles = Regex.Matches(content, @"<(?:[^:]+:)?Profiles[^>]+token=""([^""]+)""");
        if (profiles.Count == 0)
        {
            Console.WriteLine("[ONVIF] No profiles found.");
            return;
        }

        foreach (Match p in profiles)
        {
            string token = p.Groups[1].Value;
            Console.WriteLine($"[ONVIF] Profile Token: {token}");

            // 3. Get Stream URI
            string getStreamUri = $@"<?xml version=""1.0"" encoding=""utf-8""?>
                <soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope"" xmlns:trt=""http://www.onvif.org/ver10/media/wsdl"" xmlns:tt=""http://www.onvif.org/ver10/schema"">
                  <soap:Body>
                    <trt:GetStreamUri>
                      <trt:StreamSetup><tt:Stream>RTP-Unicast</tt:Stream><tt:Transport><tt:Protocol>RTSP</tt:Protocol></tt:Transport></trt:StreamSetup>
                      <trt:ProfileToken>{token}</trt:ProfileToken>
                    </trt:GetStreamUri>
                  </soap:Body>
                </soap:Envelope>";

            resp = await http.PostAsync(mediaServiceUrl, new StringContent(getStreamUri, Encoding.UTF8, "application/soap+xml"));
            string uriContent = await resp.Content.ReadAsStringAsync();
            var uriMatch = Regex.Match(uriContent, @"<(?:[^:]+:)?Uri>([^<]+)</(?:[^:]+:)?Uri>");
            if (uriMatch.Success)
            {
                Console.WriteLine($"[ONVIF] STREAM URI for {token}: {uriMatch.Groups[1].Value}");
            }
        }
    }
}
