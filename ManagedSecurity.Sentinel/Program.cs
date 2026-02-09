using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using ManagedSecurity.Core;
using ManagedSecurity.Common;

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
                _ => PrintUsage()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
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
        Console.WriteLine("  sentinel inspect <src>");
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

    static int DoRecord(string[] args)
    {
        if (args.Length < 4) return PrintUsage();
        string srcPath = args[1];
        string targetDir = args[2];
        string password = args[3];
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
        string targetDir = args[2];
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

    static byte[] DeriveKey(string password)
    {
        // For a real app, use a unique salt stored in the file. 
        // For this demo, we use a fixed salt.
        byte[] salt = "ManagedSecurity_Static_Salt_123"u8.ToArray();
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32);
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
}

[System.Text.Json.Serialization.JsonSerializable(typeof(List<VaultEntry>))]
internal partial class SentinelJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
