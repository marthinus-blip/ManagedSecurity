using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ManagedSecurity.Discovery;

public class RtspScanner
{
    private readonly int _timeoutMs;

    public RtspScanner(int timeoutMs = 1500)
    {
        _timeoutMs = timeoutMs;
    }

    public async Task<List<DiscoveryResult>> ScanSubnetAsync(string subnetPrefix, int startHost = 1, int endHost = 254)
    {
        var results = new ConcurrentBag<DiscoveryResult>();
        var tasks = new List<Task>();

        for (int i = startHost; i <= endHost; i++)
        {
            string ip = $"{subnetPrefix}.{i}";
            tasks.Add(ScanHostAsync(ip, results));
        }

        await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task ScanHostAsync(string ip, ConcurrentBag<DiscoveryResult> results)
    {
        foreach (int port in RtspWellKnown.DefaultPorts)
        {
            if (await IsPortOpenAsync(ip, port))
            {
                // Found an open port, now probe for RTSP paths
                var result = await ProbeRtspPortAsync(ip, port);
                if (result != null)
                {
                    results.Add(result);
                    break; // Port found, move to next host
                }
            }
        }
    }

    private async Task<bool> IsPortOpenAsync(string ip, int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ip, port);
            var timeoutTask = Task.Delay(_timeoutMs);

            if (await Task.WhenAny(connectTask, timeoutTask) == connectTask)
            {
                await connectTask; // Propagate exceptions
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<DiscoveryResult?> ProbeRtspPortAsync(string ip, int port)
    {
        foreach (string path in RtspWellKnown.CommonPaths)
        {
            var status = await CheckPathAsync(ip, port, path);
            if (status.Found)
            {
                return new DiscoveryResult(ip, port, path) { RequiresAuth = status.AuthRequired };
            }
        }

        return null;
    }

    private async Task<(bool Found, bool AuthRequired)> CheckPathAsync(string ip, int port, string path)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(ip, port);
            using var stream = client.GetStream();
            stream.ReadTimeout = _timeoutMs;

            // USE DESCRIBE instead of OPTIONS for better stream verification
            string request = $"DESCRIBE rtsp://{ip}:{port}{path} RTSP/1.0\r\n" +
                             $"CSeq: 1\r\n" +
                             $"User-Agent: SentinelDiscovery/1.0\r\n\r\n";

            byte[] buffer = Encoding.UTF8.GetBytes(request);
            await stream.WriteAsync(buffer);

            byte[] responseBuffer = new byte[2048];
            int read = await stream.ReadAsync(responseBuffer);
            string response = Encoding.UTF8.GetString(responseBuffer, 0, read);

            if (response.Contains("RTSP/1.0 200 OK"))
            {
                return (true, false);
            }
            if (response.Contains("RTSP/1.0 401 Unauthorized"))
            {
                return (true, true);
            }

            // Some cameras return 404 for DESCRIBE on / but 200 for OPTIONS.
            // We only count it as "Found" if it's 200 or 401 (Auth required).
            return (false, false);
        }
        catch
        {
            return (false, false);
        }
    }
}
