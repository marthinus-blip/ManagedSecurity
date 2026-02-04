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
        Console.WriteLine("  sentinel listen <port> <target-dir> <camera-id>");
        Console.WriteLine("  sentinel transmit <src> <host> <port> <camera-id>");
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

        // Metadata: CameraID | Timestamp
        string metadataStr = $"CameraID={cameraId};Timestamp={DateTimeOffset.UtcNow:O}";
        byte[] metadata = Encoding.UTF8.GetBytes(metadataStr);

        using var src = File.OpenRead(srcPath);
        using var dst = File.Create(dstPath);
        using var crypto = new ManagedSecurityStream(dst, cipher, ManagedSecurityStreamMode.Encrypt, metadata: metadata);

        Console.WriteLine($"Archiving {srcPath} to {dstPath}...");
        src.CopyTo(crypto);
        
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

        using var src = File.OpenRead(srcPath);
        
        // Manual peek at the Master Header to show Metadata without a key
        byte[] header = new byte[14];
        if (src.Read(header) < 14) throw new Exception("Tuncated master header.");

        if (header[0] != 'M' || header[1] != 'S' || header[2] != 'G')
            throw new Exception("Not a ManagedSecurity archive.");

        ushort metaLen = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(12));
        if (metaLen > 0)
        {
            byte[] meta = new byte[metaLen];
            src.Read(meta);
            Console.WriteLine($"[DISCOVERY] Metadata: {Encoding.UTF8.GetString(meta)}");
        }
        else
        {
            Console.WriteLine("[DISCOVERY] No metadata found.");
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

            // SIMULATION: If we find something that looks like an H.264 I-Frame (e.g. byte 0xFF in this dummy)
            // we flush to a frame to ensure seekability.
            bool isIFrameSim = buffer.AsSpan(0, read).Contains((byte)0xFF);
            
            currentStream!.Write(buffer, 0, read);
            bytesInFile += read;

            if (isIFrameSim)
            {
                // Force a frame boundary for video seekability
                currentStream.FlushToFrame();
                Console.WriteLine("  [VIDEO] I-Frame detected, flushing cryptographic frame.");
            }
        }

        currentStream?.Dispose();
        Console.WriteLine("Recording stopped.");
        return 0;
    }

    static async Task<int> DoListenAsync(string[] args)
    {
        if (args.Length < 4) return PrintUsage();
        int port = int.Parse(args[1]);
        string targetDir = args[2];
        string cameraId = args[3];

        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"[HUB] Listening on port {port} for Camera {cameraId}...");

        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync();
            Console.WriteLine($"[HUB] Connection received from {client.Client.RemoteEndPoint}");

            try
            {
                using var networkStream = client.GetStream();
                using var shield = new ShieldSession();
                
                Console.WriteLine("[HUB] Performing Handshake...");
                byte[] sessionKey = await shield.PerformHandshakeAsync(networkStream);
                Console.WriteLine("[HUB] Handshake successful. Session key established.");

                var cipher = new Cipher(new SimpleKeyProvider(sessionKey));
                
                string fileName = $"Sentinel_{cameraId}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.msg";
                string fullPath = Path.Combine(targetDir, fileName);
                
                using var fs = File.Create(fullPath);
                using var crypto = new ManagedSecurityStream(networkStream, cipher, ManagedSecurityStreamMode.Decrypt);
                
                // Read decryption metadata (Discovery)
                // In a real stream, we'd loop until connection close
                byte[] smallBuffer = new byte[64 * 1024];
                while (true)
                {
                    int read = await crypto.ReadAsync(smallBuffer);
                    if (read == 0) break;
                    await fs.WriteAsync(smallBuffer.AsMemory(0, read));
                }
                
                Console.WriteLine($"[HUB] Session complete. Archive saved to {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUB] Session error: {ex}");
            }
        }
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
        
        Console.WriteLine("[CAMERA] Transmitting secure stream...");
        await src.CopyToAsync(crypto);
        
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

    private class SimpleKeyProvider : IKeyProvider
    {
        private readonly ReadOnlyMemory<byte> _key;
        public SimpleKeyProvider(byte[] key) => _key = key;
        public ReadOnlyMemory<byte> GetKey(int keyIndex) => _key;
    }
}
