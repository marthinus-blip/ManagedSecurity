using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ManagedSecurity.Core;
using ManagedSecurity.Common;
using ManagedSecurity.Discovery;
using ManagedSecurity.Orchestration;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ManagedSecurity.Common.Logging;

namespace ManagedSecurity.Sentinel;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            return args[0].ToLower() switch
            {
                "ingest" => DoIngest(args),
                "record" => DoRecord(args),
                "extract" => DoExtract(args),
                "inspect" => DoInspect(args),
                "index" => DoIndex(args),
                "search" => DoSearch(args),
                "listen" => await DoListenAsync(args),
                "transmit" => await DoTransmitAsync(args),
                "scan" => await DoScanAsync(args),
                "agent" => await DoAgentAsync(args),
                "version" => PrintVersion(),
                "live-stream" => await DoLiveStreamAsync(args),

                "onvif-diag" => await DoOnvifDiag(args),
                _ => PrintUsage()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static int PrintVersion()
    {
        var version = typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "Unknown";

        Console.WriteLine($"ManagedSecurity Sentinel v{version}");
        return 0;
    }

    static int PrintUsage()
    {
        Console.WriteLine("ManagedSecurity Sentinel - Secure Video/File Archiver");
        Console.WriteLine("\nUsage:");
        Console.WriteLine("  sentinel ingest <src> <dst> <password> [camera-id]");
        Console.WriteLine("  sentinel record <src> <target-dir> <password> [camera-id]");
        Console.WriteLine("  sentinel extract <src> <dst> <password>");
        Console.WriteLine("  sentinel index <dir>");
        Console.WriteLine("  sentinel search <dir> <query>");
        Console.WriteLine("  sentinel scan <subnet-prefix>");
        Console.WriteLine("  sentinel agent <subnet-prefix> [role: commander|scout]");
        Console.WriteLine("  sentinel inspect <src>");
        Console.WriteLine("  sentinel live-stream <port> <password> <source-url> [camera-id]");
        Console.WriteLine("  sentinel version");

        return 1;
    }

    static int DoIngest(string[] args)
    {
        if (args.Length < 4) return PrintUsage();
        string srcPath = args[1];
        string dstPath = args[2];
        string password = args[3];
        string cameraId = args.Length > 4 ? args[4] : "Default_Camera";

        byte[] key = DeriveKey(password);
        var cipher = new Cipher(new SimpleKeyProvider(key));

        string metadataStr = $"CameraID={cameraId};Timestamp={DateTimeOffset.UtcNow:O}";
        byte[] metadata = Encoding.UTF8.GetBytes(metadataStr);

        using var src = File.OpenRead(srcPath);
        using var dst = File.Create(dstPath);
        using var crypto = new ManagedSecurityStream(dst, cipher, ManagedSecurityStreamMode.Encrypt, metadata: metadata);

        Console.WriteLine($"Archiving {srcPath} to {dstPath} with NAL alignment...");
        
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = src.Read(buffer)) > 0)
        {
            var span = buffer.AsSpan(0, read);
            int lastPos = 0;

            foreach (var (offset, type) in NalUnitScanner.Scan(span))
            {
                if (NalUnitScanner.IsSyncPoint(type))
                {
                    // Write data up to the NAL unit
                    if (offset > lastPos)
                    {
                        crypto.Write(span.Slice(lastPos, offset - lastPos));
                    }
                    
                    // Flush current cryptographic frame so the NEXT frame starts exactly with this NAL unit
                    crypto.FlushToFrame();
                    lastPos = offset;
                }
            }

            // Write remaining data in buffer
            if (lastPos < read)
            {
                crypto.Write(span.Slice(lastPos));
            }
        }
        
        Console.WriteLine("Ingest complete.");
        return 0;
    }

    static int DoExtract(string[] args)
    {
        if (args.Length < 4) return PrintUsage();
        string srcPath = args[1];
        string dstPath = args[2];
        string password = args[3];

        byte[] key = DeriveKey(password);
        var cipher = new Cipher(new SimpleKeyProvider(key));

        using var src = File.OpenRead(srcPath);
        using var crypto = new ManagedSecurityStream(src, cipher, ManagedSecurityStreamMode.Decrypt);
        
        // Trigger header read
        byte[] buffer = new byte[1];
        crypto.Read(buffer, 0, 0); 

        if (crypto.Metadata != null)
        {
            Console.WriteLine($"Archive Metadata: {Encoding.UTF8.GetString(crypto.Metadata)}");
        }

        using var dst = File.Create(dstPath);
        Console.WriteLine($"Extracting {srcPath} to {dstPath}...");
        crypto.CopyTo(dst);

        Console.WriteLine("Extraction complete.");
        return 0;
    }

    static int DoInspect(string[] args)
    {
        if (args.Length < 2) return PrintUsage();
        string srcPath = args[1];

        var entry = VaultIndexer.TryGetEntry(srcPath);
        if (entry == null)
        {
            Console.Error.WriteLine("Error: Could not read master header.");
            return 1;
        }

        Console.WriteLine($"[DISCOVERY] File: {entry.FileName}");
        Console.WriteLine($"[DISCOVERY] ChunkSize: {entry.ChunkSize}");
        Console.WriteLine($"[DISCOVERY] KeyIndex: {entry.KeyIndex}");
        Console.WriteLine($"[DISCOVERY] Metadata: {entry.Metadata}");
        Console.WriteLine($"[DISCOVERY] SeekTableOffset: {entry.SeekTableOffset}");
        
        foreach (var tag in entry.Tags)
        {
            Console.WriteLine($"  Tag: {tag.Key} = {tag.Value}");
        }

        return 0;
    }

    static int DoIndex(string[] args)
    {
        if (args.Length < 2) return PrintUsage();
        string dir = args[1];
        string? outputPath = args.Length > 2 ? args[2] : null;

        Console.WriteLine($"[INDEX] Scanning {dir}...");
        var entries = VaultIndexer.ScanDirectory(dir).ToList();
        
        if (string.IsNullOrEmpty(outputPath))
        {
            Console.WriteLine($"[INDEX] Found {entries.Count} ManagedSecurity archives:");
            foreach (var e in entries)
            {
                Console.WriteLine($"- {e.FileName} [{e.Metadata}]");
            }
        }
        else
        {
            // If we are exporting to a file (like in Blazor wwwroot), 
            // we should make paths relative to that file's directory
            string outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".";
            foreach (var e in entries)
            {
                e.FullPath = Path.GetRelativePath(outputDir, e.FullPath).Replace("\\", "/");
            }

            var options = new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true,
                TypeInfoResolver = SentinelJsonContext.Default
            };
            string json = System.Text.Json.JsonSerializer.Serialize(entries, options);
            File.WriteAllText(outputPath, json);
            Console.WriteLine($"[INDEX] Exported {entries.Count} entries to {outputPath}");
        }


        return 0;
    }

    static int DoSearch(string[] args)
    {
        if (args.Length < 3) return PrintUsage();
        string dir = args[1];
        string query = args[2];

        Console.WriteLine($"[SEARCH] Hunting for '{query}' in {dir}...");
        var all = VaultIndexer.ScanDirectory(dir);
        var results = VaultIndexer.Search(all, query).ToList();

        Console.WriteLine($"[SEARCH] Found {results.Count} matches:");
        foreach (var r in results)
        {
            Console.WriteLine($"- {r.FileName}");
            Console.WriteLine($"  Metadata: {r.Metadata}");
        }

        return 0;
    }

    static async Task<int> DoScanAsync(string[] args)
    {
        if (args.Length < 2) return PrintUsage();
        string subnet = args[1];
        string? exportPath = args.Length > 2 ? args[2] : null;

        Console.WriteLine("[DISCOVERY] Phase 1: Sending ONVIF Multicast Probe (WS-Discovery)...");
        var onvif = new OnvifDiscovery();
        var onvifDevices = await onvif.ProbeAsync();
        
        if (onvifDevices.Count > 0)
        {
            Console.WriteLine($"[DISCOVERY] ONVIF self-reported {onvifDevices.Count} devices:");
            foreach (var d in onvifDevices) 
            {
                string info = "";
                if (!string.IsNullOrEmpty(d.Model)) info += $" [Model: {d.Model}]";
                if (!string.IsNullOrEmpty(d.Name)) info += $" [Name: {d.Name}]";
                Console.WriteLine($"- {d.IpAddress}{info}");
            }
        }

        Console.WriteLine($"\n[DISCOVERY] Phase 2: Scanning subnet {subnet}.0/24 on common vendor ports...");
        var scanner = new RtspScanner();
        var results = await scanner.ScanSubnetAsync(subnet);

        // Merge ONVIF metadata into RTSP results
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var match = onvifDevices.FirstOrDefault(o => o.IpAddress == r.IpAddress);
            if (match != null)
            {
                r.Vendor = match.Name;
                r.Model = match.Model;
            }
        }

        Console.WriteLine($"[DISCOVERY] Found {results.Count} active RTSP streams:");
        foreach (var r in results)
        {
            string authLabel = r.RequiresAuth ? "[AUTH REQUIRED]" : "[OPEN]";
            string meta = r.Model != null ? $" ({r.Vendor} {r.Model})" : "";
            Console.WriteLine($"- {r.Url}{meta} {authLabel}");
        }

        if (!string.IsNullOrEmpty(exportPath))
        {
            var options = new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true,
                TypeInfoResolver = SentinelJsonContext.Default
            };
            string json = System.Text.Json.JsonSerializer.Serialize(results, options);
            File.WriteAllText(exportPath, json);
            Console.WriteLine($"\n[DISCOVERY] Exported {results.Count} results to {exportPath}");
        }

        return 0;
    }



    static int DoRecord(string[] args)
    {
        if (args.Length < 4) return PrintUsage();
        string srcPath = args[1];
        string targetDir = args.Length > 2 ? args[2] : Paths.GetVaultPath();
        string password = args.Length > 3 ? args[3] : "p@ssword";
        string cameraId = args.Length > 4 ? args[4] : "Default_Camera";

        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

        byte[] key = DeriveKey(password);
        var cipher = new Cipher(new SimpleKeyProvider(key));

        using var src = File.OpenRead(srcPath);
        byte[] buffer = new byte[8192];
        
        ManagedSecurityStream? currentStream = null;
        DateTimeOffset lastRoll = DateTimeOffset.MinValue;
        int bytesInFile = 0;

        Console.WriteLine($"Starting Rolling Record to {targetDir}...");

        while (true)
        {
            int read = src.Read(buffer);
            if (read == 0) break;

            // Rolling Logic: First run OR Every 5 seconds OR every 200KB
            bool shouldRoll = currentStream == null || 
                              (DateTimeOffset.UtcNow - lastRoll).TotalSeconds > 5 || 
                              bytesInFile > 200 * 1024;

            if (shouldRoll)
            {
                currentStream?.Dispose();
                
                string fileName = $"Sentinel_{cameraId}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString().Substring(0,4)}.msg";
                string fullPath = Path.Combine(targetDir, fileName);
                
                var fs = File.Create(fullPath);
                string meta = $"CameraID={cameraId};StartTime={DateTimeOffset.UtcNow:O}";
                currentStream = new ManagedSecurityStream(fs, cipher, ManagedSecurityStreamMode.Encrypt, metadata: Encoding.UTF8.GetBytes(meta));
                
                lastRoll = DateTimeOffset.UtcNow;
                bytesInFile = 0;
                Console.WriteLine($"[ROLL] New Segment: {fileName}");
            }

            // NAL-Aware Flushing:
            // Look for I-Frames (Sync points) to ensure precise seek entry points
            var span = buffer.AsSpan(0, read);
            int lastPos = 0;

            foreach (var (offset, type) in NalUnitScanner.Scan(span))
            {
                if (NalUnitScanner.IsSyncPoint(type))
                {
                    if (offset > lastPos)
                    {
                        currentStream!.Write(span.Slice(lastPos, offset - lastPos));
                    }
                    
                    currentStream!.FlushToFrame();
                    lastPos = offset;
                    Console.WriteLine($"  [VIDEO] Sync point ({type}) detected at offset {bytesInFile + offset}, aligning frame.");
                }
            }

            if (lastPos < read)
            {
                currentStream!.Write(span.Slice(lastPos));
            }
            
            bytesInFile += read;
        }

        currentStream?.Dispose();
        Console.WriteLine("Recording stopped.");
        return 0;
    }

    static async Task<int> DoListenAsync(string[] args)
    {
        if (args.Length < 3) return PrintUsage();
        int port = int.Parse(args[1]);
        string targetDir = args.Length > 2 ? args[2] : Paths.GetVaultPath();
        string defaultCameraId = args.Length > 3 ? args[3] : "Unknown_Camera";

        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n[HUB] Shutdown requested...");
            cts.Cancel();
        };

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"[HUB] Listening on port {port}. Press Ctrl+C to stop.");

        var tasks = new List<Task>();

        try
        {
            while (!cts.IsCancellationRequested)
            {
                // Accept incoming connections
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                string clientId = client.Client.RemoteEndPoint?.ToString() ?? Guid.NewGuid().ToString().Substring(0, 8);
                Log(clientId, "Connection received.");

                // Spawn task for this client
                var task = HandleClientAsync(client, targetDir, defaultCameraId, cts.Token);
                tasks.Add(task);

                // Cleanup finished tasks occasionally
                tasks.RemoveAll(t => t.IsCompleted);
            }
        }
        finally
        {
            listener.Stop();
            Log("SYSTEM", "Waiting for active sessions to close...");
            await Task.WhenAll(tasks);
        }

        return 0;
    }

    static async Task HandleClientAsync(TcpClient client, string targetDir, string defaultCameraId, CancellationToken ct)
    {
        string remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
        using (client)
        {
            try
            {
                using var networkStream = client.GetStream();
                using var shield = new ShieldSession();

                // 1. Handshake with Timeout
                Log(remoteEndPoint, "Performing Handshake...");
                using var handshakeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, handshakeTimeout.Token);
                
                byte[] sessionKey;
                try
                {
                    sessionKey = await shield.PerformHandshakeAsync(networkStream, linkedCts.Token);
                }
                catch (OperationCanceledException) when (handshakeTimeout.IsCancellationRequested)
                {
                    Log(remoteEndPoint, "Handshake Timed Out.");
                    return;
                }

                Log(remoteEndPoint, "Handshake successful.");

                // 2. Initialize Internal Decryption (to verify incoming stream)
                var cipher = new Cipher(new SimpleKeyProvider(sessionKey));
                using var cryptoIn = new ManagedSecurityStream(networkStream, cipher, ManagedSecurityStreamMode.Decrypt);

                // Read 0 bytes to force master header processing
                await cryptoIn.ReadAsync(new byte[1], 0, 0, ct);

                string cameraId = defaultCameraId;
                if (cryptoIn.Metadata != null)
                {
                    string meta = Encoding.UTF8.GetString(cryptoIn.Metadata);
                    foreach (var part in meta.Split(';'))
                    {
                        if (part.StartsWith("CameraID="))
                        {
                            cameraId = part.Substring(9).Replace("/", "_").Replace("\\", "_");
                            break;
                        }
                    }
                }

                Log(remoteEndPoint, $"Session active for camera: {cameraId}");

                // 3. Stream to Rolling Vault using ENCRYPTION mode to create a valid .msg file
                string fileName = $"Sentinel_{cameraId}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.msg";
                string fullPath = Path.Combine(targetDir, fileName);

                using (var fs = File.Create(fullPath))
                using (var cryptoOut = new ManagedSecurityStream(fs, cipher, ManagedSecurityStreamMode.Encrypt, metadata: cryptoIn.Metadata))
                {
                    byte[] buffer = new byte[64 * 1024];
                    int read;
                    while ((read = await cryptoIn.ReadAsync(buffer, ct)) > 0)
                    {
                        var syncPoints = GetSyncPoints(buffer, read);
                        int lastPos = 0;

                        foreach (var (offset, type) in syncPoints)
                        {
                            if (offset > lastPos)
                            {
                                await cryptoOut.WriteAsync(buffer.AsMemory(lastPos, offset - lastPos), ct);
                            }
                            
                            await cryptoOut.FlushToFrameAsync();
                            lastPos = offset;
                        }

                        if (lastPos < read)
                        {
                            await cryptoOut.WriteAsync(buffer.AsMemory(lastPos, read - lastPos), ct);
                        }
                    }
                }


                Log(remoteEndPoint, $"Session complete. Saved and verified archive: {fileName}");
            }
            catch (Exception ex)
            {
                Log(remoteEndPoint, $"Session error: {ex.Message}");
            }
        }
    }

    static void Log(string client, string message)
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{client}] {message}");
    }

    static async Task<int> DoTransmitAsync(string[] args)
    {
        if (args.Length < 5) return PrintUsage();
        string srcPath = args[1];
        string host = args[2];
        int port = int.Parse(args[3]);
        string cameraId = args[4];

        using var src = File.OpenRead(srcPath);
        using var client = new TcpClient();
        
        Console.WriteLine($"[CAMERA] Connecting to {host}:{port}...");
        await client.ConnectAsync(host, port);
        
        using var networkStream = client.GetStream();
        using var shield = new ShieldSession();
        
        Console.WriteLine("[CAMERA] Performing Handshake...");
        byte[] sessionKey = await shield.PerformHandshakeAsync(networkStream);
        Console.WriteLine("[CAMERA] Handshake successful.");

        var cipher = new Cipher(new SimpleKeyProvider(sessionKey));
        string meta = $"CameraID={cameraId};StartTime={DateTimeOffset.UtcNow:O}";
        
        using var crypto = new ManagedSecurityStream(networkStream, cipher, ManagedSecurityStreamMode.Encrypt, metadata: Encoding.UTF8.GetBytes(meta));
        
        Console.WriteLine("[CAMERA] Transmitting secure stream (NAL-Aware)...");
        
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = await src.ReadAsync(buffer)) > 0)
        {
            var syncPoints = GetSyncPoints(buffer, read);
            int lastPos = 0;

            foreach (var (offset, type) in syncPoints)
            {
                if (offset > lastPos)
                {
                    await crypto.WriteAsync(buffer.AsMemory(lastPos, offset - lastPos));
                }
                
                await crypto.FlushToFrameAsync();
                lastPos = offset;
            }

            if (lastPos < read)
            {
                await crypto.WriteAsync(buffer.AsMemory(lastPos, read - lastPos));
            }
        }

        
        Console.WriteLine("[CAMERA] Transmission complete.");
        return 0;
    }

    static async Task<int> DoAgentAsync(string[] args)
    {
        if (args.Length < 2) return PrintUsage();
        string subnetOrConfig = args[1];
        string role = args.Length > 2 ? args[2].ToLower() : "both";
        string password = args.Length > 3 ? args[3] : "p@ssword";
        string user = args.Length > 4 ? args[4] : "admin"; 

        var version = typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "Unknown";
        
        Console.WriteLine($"[SENTINEL] Starting Agent v{version}...");
        
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Trace));
        SentinelLogger.Initialize(loggerFactory);

        byte[] key = DeriveKey(password);
        var cipher = new Cipher(new SimpleKeyProvider(key));
        
        var agent = new SentinelAgent();
        var config = new OrchestrationConfig();
        string configFilePath = Path.GetFullPath(Paths.GetRuntimePath("managed_cameras.json"));
        Console.WriteLine($"[SENTINEL] Runtime Data: {Path.GetFullPath(Paths.RuntimeData)}");
        Console.WriteLine($"[SENTINEL] Config File: {configFilePath}");
        
        var store = new CameraStore(configFilePath);
        store.SetOptions(new JsonSerializerOptions { TypeInfoResolver = SentinelJsonContext.Default, WriteIndented = true });
        
        var commander = role == "commander" || role == "both" ? new CommanderBehavior(config, store) : null;
        var guardian = role == "scout" || role == "both" ? new GuardianBehavior(
            agent.Id, 
            config, 
            "http://localhost:5188",
            hb => commander?.ReceiveHeartbeat(hb),
            alert => commander?.ReceiveAlert(alert),
            cipher) : null;

        // Domain Discovery (Ghost Sentinel detection)
        var domain = new ManagedSecurity.Orchestration.DomainBehavior(agent.Id, version, role, new JsonSerializerOptions { TypeInfoResolver = SentinelJsonContext.Default });
        await agent.AddBehaviorAsync(domain);

        if (commander != null)
        {
            await commander.InitializeFromStoreAsync();
            await agent.AddBehaviorAsync(commander);
        }

        if (guardian != null)
        {
            if (commander != null)
            {
                commander.OnTaskAssigned += (workerId, assignment) => 
                {
                    if (workerId == agent.Id) guardian.AcceptAssignment(assignment);
                };
            }
            await agent.AddBehaviorAsync(guardian);
        }

        if (commander != null && subnetOrConfig.Contains('.'))
        {
            // Subnet Validation
            var localIps = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Where(ua => ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(ua => ua.Address.ToString())
                .ToList();

            if (!localIps.Any(ip => ip.StartsWith(subnetOrConfig)))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[WARNING] Prefix '{subnetOrConfig}' does not match any local network interfaces.");
                Console.WriteLine($"[WARNING] Local IPs: {string.Join(", ", localIps)}");
                Console.WriteLine("[WARNING] Verification failed. Scanning may result in zero discoverable devices.");
                Console.ResetColor();
                
                // Optional: ask for confirmation or just continue with warning
            }

            commander.EnableAutoDiscovery(subnetOrConfig);
        }

        // 2. Start Governor API and Live Stream Server
        if (commander != null)
        {
             _ = Task.Run(async () => {
                try {
                    await StartGovernorApiAsync(commander, user, password, agent.Id);
                } catch (Exception ex) {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[CRITICAL] Governor API failed to start: {ex.Message}");
                    Console.ResetColor();
                }
             });
        }

        // Wait for cancellation
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
        
        try
        {
            await Task.Delay(-1, cts.Token);
        }
        catch (OperationCanceledException) { }

        await agent.ShutdownAsync();
        Console.WriteLine("[AGENT] Offline.");
        return 0;
    }

    static async Task StartGovernorApiAsync(CommanderBehavior commander, string user, string pass, string agentId)
    {
        int port = 5188;
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://*:{port}/");
        listener.Start();
        Console.WriteLine($"[GOVERNOR] Agent API & Live Server active at http://localhost:{port}/");

        byte[] key = DeriveKey(pass);
        var cipher = new Cipher(new SimpleKeyProvider(key));

        while (true)
        {
            try
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(async () =>
                {
                    var req = context.Request;
                    var resp = context.Response;
                    resp.Headers.Add("Access-Control-Allow-Origin", "*");
                    resp.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                    resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                    if (req.HttpMethod == "OPTIONS") { resp.Close(); return; }

                    string url = req.Url?.AbsolutePath ?? "";
                    Console.WriteLine($"[GOVERNOR] {req.HttpMethod} {url}");
                    Console.Out.Flush();

                    if (url.Equals("/api/discovery", StringComparison.OrdinalIgnoreCase) && req.HttpMethod == "GET")
                    {
                        var cameras = commander.GetCameras();
                        var options = new System.Text.Json.JsonSerializerOptions { TypeInfoResolver = SentinelJsonContext.Default };
                        byte[] json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(cameras, options);
                        resp.ContentType = "application/json";
                        await resp.OutputStream.WriteAsync(json);
                        resp.Close();
                    }
                    else if (url == "/api/config/configure" && req.HttpMethod == "POST")
                    {
                        try 
                        {
                            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, TypeInfoResolver = SentinelJsonContext.Default };
                            var configReq = await System.Text.Json.JsonSerializer.DeserializeAsync<ConfigPayload>(req.InputStream, options);

                            if (configReq != null && !string.IsNullOrEmpty(configReq.Url) && !string.IsNullOrEmpty(configReq.DisplayName)) 
                            {
                                commander.ConfigureCamera(configReq.Url, configReq.DisplayName);
                                Console.WriteLine($"[GOVERNOR] Camera configured: {configReq.DisplayName} ({configReq.Url})");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[GOVERNOR] Error parsing config POST: {ex.Message}");
                        }
                        resp.StatusCode = 200;
                        resp.Close();
                    }
                    else if (url.Equals("/api/scan", StringComparison.OrdinalIgnoreCase) && req.HttpMethod == "POST")
                    {
                        Console.WriteLine("[GOVERNOR] Manual Radar Scan requested.");
                        commander.TriggerManualScan();
                        resp.StatusCode = 200;
                        resp.Close();
                    }
                    else if (url.StartsWith("/api/snapshot/", StringComparison.OrdinalIgnoreCase) && req.HttpMethod == "GET")
                    {
                        string cameraId = url.Substring(14).TrimEnd('/');
                        var cameras = commander.GetCameras();
                        var cam = cameras.FirstOrDefault(c => string.Equals(c.Id, cameraId, StringComparison.OrdinalIgnoreCase) || string.Equals(c.DisplayName, cameraId, StringComparison.OrdinalIgnoreCase));
                        
                        if (cam == null)
                        {
                            Console.WriteLine($"[GOVERNOR] Snapshot 404: {cameraId}");
                            resp.StatusCode = 404;
                            resp.Close();
                            return;
                        }

                        bool needsAuth = cam.RequiresAuth;
                        await HandleSnapshotRequest(context, cam.Url, cam.DisplayName ?? cam.Id, needsAuth ? user : null, needsAuth ? pass : null, cipher);
                    }
                    else if (url.StartsWith("/stream/", StringComparison.OrdinalIgnoreCase) && req.HttpMethod == "GET")
                    {
                        string cameraId = url.Substring(8).TrimEnd('/');
                        var cameras = commander.GetCameras();
                        var cam = cameras.FirstOrDefault(c => string.Equals(c.Id, cameraId, StringComparison.OrdinalIgnoreCase) || string.Equals(c.DisplayName, cameraId, StringComparison.OrdinalIgnoreCase));
                        
                        if (cam == null)
                        {
                            Console.WriteLine($"[GOVERNOR] Stream 404: {cameraId}");
                            resp.StatusCode = 404;
                            resp.Close();
                            return;
                        }

                        bool needsAuth = cam.RequiresAuth;
                        await HandleLiveStreamRequest(context, cam.Url, cam.DisplayName ?? cam.Id, needsAuth ? user : null, needsAuth ? pass : null, cipher);
                    }
                    else
                    {
                        resp.StatusCode = 404;
                        resp.Close();
                    }
                });
            }
            catch { }
        }
    }

    static async Task HandleSnapshotRequest(HttpListenerContext context, string sourceUrl, string cameraId, string? globalUser, string? globalPass, Cipher cipher)
    {
        try
        {
            Console.WriteLine($"[SNAPSHOT] Secure frame capture for {cameraId}...");
            
            // Inject credentials if missing
            string authenticatedUrl = sourceUrl;
            if (!string.IsNullOrEmpty(globalUser) && !sourceUrl.Contains("@"))
            {
                authenticatedUrl = sourceUrl.Replace("rtsp://", $"rtsp://{globalUser}:{globalPass}@");
            }

            // Capture to a memory stream first so we can encrypt it as a single block
            using var ms = new MemoryStream();
            bool success = await TryCaptureSnapshot(ms, authenticatedUrl);
            
            if (!success || ms.Length == 0)
            {
                Console.WriteLine($"[SNAPSHOT] Signal lost or empty for {cameraId} (Len={ms.Length}). Sending fallback pattern (Secure).");
                ms.SetLength(0);
                await TryCaptureSnapshot(ms, "test"); 
            }

            byte[] rawJpeg = ms.ToArray();
            Console.WriteLine($"[SNAPSHOT] Captured {rawJpeg.Length} bytes for {cameraId}");
            
            // Encrypt the snapshot (Master Key Index = 0)
            byte[] encrypted = cipher.Encrypt(rawJpeg, 0); 

            context.Response.ContentType = "application/octet-stream";
            context.Response.ContentLength64 = encrypted.Length;
            await context.Response.OutputStream.WriteAsync(encrypted);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SNAPSHOT] Failed to handle secure snapshot for {cameraId}: {ex.Message}\n{ex.StackTrace}");
            context.Response.Close();
        }
    }

    static async Task<bool> TryCaptureSnapshot(Stream output, string url)
    {
        string pipeline;
        if (url.ToLower().Contains("test") || url == "test")
        {
            pipeline = "videotestsrc num-buffers=1 pattern=smpte ! video/x-raw,width=800,height=450 ! jpegenc ! fdsink fd=1";
        }
        else 
        {
            // Use a short timeout for uridecodebin and grab the first frame
            pipeline = $"uridecodebin uri=\"{url}\" connection-speed=1000 ! videoconvert ! videoscale ! video/x-raw,width=800,height=450 ! jpegenc snapshot=true ! fdsink fd=1";
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "gst-launch-1.0",
            Arguments = pipeline,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null) return false;

        var copyTask = process.StandardOutput.BaseStream.CopyToAsync(output);
        var errTask = process.StandardError.ReadToEndAsync();
        
        // Timeout after 20 seconds to allow for slow pipeline initialization
        var timeout = Task.Delay(TimeSpan.FromSeconds(20));
        var completed = await Task.WhenAny(copyTask, timeout);

        if (completed == copyTask)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode == 0)
            {
                return true;
            }
        }

        try { process.Kill(); } catch { }
        
        string errorOutput = await errTask;
        if (!string.IsNullOrEmpty(errorOutput))
        {
            Console.WriteLine($"[SNAPSHOT-GST] Error: {errorOutput}");
        }

        return false;
        return false;
    }

    static async Task HandleLiveStreamRequest(HttpListenerContext context, string sourceUrl, string cameraId, string? user, string? pass, Cipher cipher)
    {
        try
        {
            Console.WriteLine($"[LIVE] Client connected for {cameraId}");
            Console.Out.Flush();
            context.Response.ContentType = "application/octet-stream";
            context.Response.SendChunked = true;

            using var responseStream = context.Response.OutputStream;
            string meta = $"CameraID={cameraId};StartTime={DateTimeOffset.UtcNow:O};Encoding=LiveStream";

            using var cryptoStream = new ManagedSecurityStream(
                responseStream,
                cipher,
                ManagedSecurityStreamMode.Encrypt,
                metadata: Encoding.UTF8.GetBytes(meta),
                leaveOpen: true);

            // [thought_streaming_stability]((2026-03-14T14:10:00) (Forcing immediate delivery of Master Header + Metadata))
            await cryptoStream.WriteAsync(Array.Empty<byte>()); 
            await responseStream.FlushAsync();

            string gstreamerCommand;
            
            // Inject credentials if missing
            string authenticatedUrl = sourceUrl;
            if (!string.IsNullOrEmpty(user) && !sourceUrl.Contains("@"))
            {
                authenticatedUrl = sourceUrl.Replace("rtsp://", $"rtsp://{user}:{pass}@");
            }

            if (sourceUrl.ToLower().Contains("test") || sourceUrl.ToLower() == "test")
            {
                // [thought_black_feed_fix]((2026-03-14T14:15:00) (Using fragmented MP4 with faststart-like behavior for non-seekable pipes.))
                gstreamerCommand = "videotestsrc is-live=true pattern=ball " +
                    "! video/x-raw,width=800,height=600,framerate=25/1 " +
                    "! clockoverlay halignment=right valignment=bottom " +
                    "! videoconvert ! x264enc tune=zerolatency bitrate=1000 speed-preset=ultrafast bframes=0 key-int-max=25 ! video/x-h264,profile=baseline " +
                    "! mp4mux streamable=true fragment-duration=100 presentation-time=true " +
                    "! fdsink fd=1";
            }
            else if (sourceUrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase))
            {
                gstreamerCommand = $"rtspsrc location=\"{authenticatedUrl}\" latency=200 protocols=tcp ! rtph264depay ! h264parse ! avdec_h264 " +
                    "! videoconvert ! videoscale ! video/x-raw,width=800,height=600 " +
                    "! queue max-size-buffers=1 leaky=downstream ! x264enc tune=zerolatency bitrate=1200 speed-preset=ultrafast key-int-max=50 ! video/x-h264,profile=baseline " +
                    "! mp4mux streamable=true fragment-duration=200 " +
                    "! fdsink fd=1";
            }
            else
            {
                gstreamerCommand = $"uridecodebin uri=\"{authenticatedUrl}\" " +
                    "! videoconvert ! videoscale ! video/x-raw,width=800,height=600 " +
                    "! x264enc tune=zerolatency bitrate=1000 speed-preset=ultrafast key-int-max=50 ! video/x-h264,profile=baseline " +
                    "! mp4mux streamable=true fragment-duration=200 " +
                    "! fdsink fd=1";
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "gst-launch-1.0",
                Arguments = gstreamerCommand,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                EnvironmentVariables = { ["GST_DEBUG"] = "3" }
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                _ = Task.Run(async () => {
                    var err = await process.StandardError.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(err)) Console.WriteLine($"[LIVE-GST] {err}");
                });

                using var gstStream = process.StandardOutput.BaseStream;
                byte[] buffer = new byte[128 * 1024];

                int read;
                bool firstChunk = true;
                int totalRead = 0;
                MemoryStream headerBuffer = new MemoryStream();

                while ((read = await gstStream.ReadAsync(buffer)) > 0)
                {
                    totalRead += read;
                    
                    if (firstChunk)
                    {
                        // [thought_mse_initialization]((2026-03-14T14:25:00) (Buffering first 16KB to ensure complete MP4 Init Segment delivery.))
                        if (totalRead < 16384)
                        {
                            await headerBuffer.WriteAsync(buffer.AsMemory(0, read));
                            continue; 
                        }
                        else
                        {
                            await headerBuffer.WriteAsync(buffer.AsMemory(0, read));
                            byte[] initialData = headerBuffer.ToArray();
                            Console.WriteLine($"[LIVE] Flushing Init Segment: {initialData.Length} bytes");
                            await cryptoStream.WriteAsync(initialData);
                            await cryptoStream.FlushToFrameAsync();
                            await responseStream.FlushAsync();
                            firstChunk = false;
                            continue;
                        }
                    }

                    try 
                    {
                        await cryptoStream.WriteAsync(buffer.AsMemory(0, read));
                        await cryptoStream.FlushToFrameAsync();
                        await responseStream.FlushAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[LIVE] Client disconnected or socket failure: {ex.Message}");
                        break; 
                    }
                }

                try { process.Kill(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LIVE] Streaming error for {cameraId}: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            context.Response.Close();
            Console.WriteLine("[LIVE] Client disconnected.");
        }
    }

    static async Task<int> DoLiveStreamAsync(string[] args)
    {
        if (args.Length < 4) return PrintUsage();
        int port = int.Parse(args[1]);
        string password = args[2];
        string sourceUrl = args[3];
        string cameraId = args.Length > 4 ? args[4] : "Live_Camera";

        byte[] key = DeriveKey(password);
        var cipher = new Cipher(new SimpleKeyProvider(key));

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://*:{port}/stream/");
        listener.Start();
        Console.WriteLine($"[LIVE] Streaming endpoint active at http://localhost:{port}/stream/ with E2EE");

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(async () => 
                {
                    try
                    {
                        Console.WriteLine($"[LIVE] Client connected: {context.Request.RemoteEndPoint}");
                        
                        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                        context.Response.ContentType = "application/octet-stream";
                        context.Response.SendChunked = true;

                        using var responseStream = context.Response.OutputStream;
                        string meta = $"CameraID={cameraId};StartTime={DateTimeOffset.UtcNow:O};Encoding=LiveStream";

                        // S=02 streaming mode as expected by the architecture.
                        using var cryptoStream = new ManagedSecurityStream(
                            responseStream, 
                            cipher, 
                            ManagedSecurityStreamMode.Encrypt, 
                            metadata: Encoding.UTF8.GetBytes(meta),
                            leaveOpen: true); 

                        // Start GStreamer sub-process to emit fragmented MP4.
                        // We use videotestsrc to simulate the live video feed from RTSP.
                        // Wait, mp4mux produces standard headers at start if not careful? 
                        // The fragmented true is needed for streams:
                        string gstreamerCommand;
                        if (sourceUrl.ToLower() == "test")
                        {
                            gstreamerCommand = "videotestsrc is-live=true pattern=ball " +
                                "! video/x-raw,width=800,height=600,framerate=30/1 " +
                                "! clockoverlay " +
                                "! x264enc tune=zerolatency bitrate=1000 ! video/x-h264,profile=baseline " +
                                "! mp4mux streamable=true fragment-duration=500 " +
                                "! fdsink fd=1";
                        }
                        else
                        {
                            gstreamerCommand = $"uridecodebin uri=\"{sourceUrl}\" " +
                                "! videoscale ! video/x-raw,width=800,height=600 " +
                                "! x264enc tune=zerolatency bitrate=1000 ! video/x-h264,profile=baseline " +
                                "! mp4mux streamable=true fragment-duration=500 " +
                                "! fdsink fd=1";
                        }

                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "gst-launch-1.0",
                            Arguments = gstreamerCommand,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true, // Hide stderr spam to prevent console flooding
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var process = System.Diagnostics.Process.Start(psi);
                        if (process != null)
                        {
                            using var gstStream = process.StandardOutput.BaseStream;
                            byte[] buffer = new byte[64 * 1024];
                            int read;
                            
                            while ((read = await gstStream.ReadAsync(buffer, cts.Token)) > 0)
                            {
                                // --- CV Middleware Hook ---
                                // e.g. Guardian.InspectFrames(...)

                                // Pass the raw output directly to E2EE
                                await cryptoStream.WriteAsync(buffer.AsMemory(0, read), cts.Token);
                                
                                // Flush frame to create sequence-bound E2EE blocks for real-time delivery
                                await cryptoStream.FlushToFrameAsync();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[LIVE] Streaming error: {ex.Message}");
                    }
                    finally
                    {
                        context.Response.Close();
                        Console.WriteLine("[LIVE] Client disconnected.");
                    }
                }, cts.Token);
            }
        }
        catch (HttpListenerException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[LIVE] Fatal Error: {ex.Message}");
        }
        finally
        {
            listener.Stop();
        }

        return 0;
    }

    static byte[] DeriveKey(string password)
    {
        // Must match JS and Ground Truth: SHA256 of the password
        byte[] passBytes = Encoding.UTF8.GetBytes(password);
        return SHA256.HashData(passBytes);
    }

    static List<(int Offset, NalUnitType Type)> GetSyncPoints(byte[] buffer, int count)
    {
        var allPoints = NalUnitScanner.Scan(buffer.AsSpan(0, count));
        var syncPoints = new List<(int, NalUnitType)>();
        foreach (var p in allPoints)
        {
            if (NalUnitScanner.IsSyncPoint(p.Type))
            {
                syncPoints.Add(p);
            }
        }
        return syncPoints;
    }

    private class SimpleKeyProvider : IKeyProvider
    {
        private readonly ReadOnlyMemory<byte> _key;
        public SimpleKeyProvider(byte[] key) => _key = key;
        public ReadOnlyMemory<byte> GetKey(int keyIndex) => _key;
    }

    static async Task<int> DoOnvifDiag(string[] args)
    {
        await OnvifDiagnostic.RunProbe();
        await OnvifDiagnostic.QueryStreamUri("192.168.8.23", "admin", "admin");
        return 0;
    }
}

internal class ConfigPayload
{
    public string? Url { get; set; }
    public string? DisplayName { get; set; }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(List<VaultEntry>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<DiscoveryResult>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DiscoveryResult))]
[System.Text.Json.Serialization.JsonSerializable(typeof(ConfigPayload))]
[System.Text.Json.Serialization.JsonSerializable(typeof(ManagedSecurity.Orchestration.DomainBehavior.PeerAnnouncement))]
internal partial class SentinelJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
