using System;
using System.Collections.Generic;

namespace ManagedSecurity.Discovery;

public enum MachineVisionRoute
{
    None = 0,
    LightPlain = 1,       // Branch Light: Polling JPEGs directly from Camera HTTP API
    HeavyPlain = 2,       // Branch Heavy (Plain): Intercept raw GStreamer RGB before Encryption (Local Only)
    HeavySensitive = 3    // Branch Heavy (Sensitive): Hook OnFrameDecrypted after E2EE transmission (Remote Worker)
}

[ManagedSecurity.Common.Attributes.AllowMagicValues]
public class DiscoveryResult
{
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? Path { get; set; }
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public bool RequiresAuth { get; set; }
    public string? DisplayName { get; set; }
    public bool IsConfigured { get; set; }
    public string Id { get; set; } = string.Empty;

    public bool IsMvEnabled { get; set; } = true;
    public MachineVisionRoute MvRoute { get; set; } = MachineVisionRoute.LightPlain;

    public DiscoveryResult() { }

    public DiscoveryResult(string ipAddress, int port, string? path = null)
    {
        IpAddress = ipAddress;
        Port = port;
        Path = path;
        Id = GenerateId(ipAddress, port, path);
    }

    private static string GenerateId(string ip, int port, string? path)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"rtsp://{ip}:{port}{path ?? "/"}"));
        return BitConverter.ToString(hash).Replace("-", "").Substring(0, 12).ToLower();
    }
    
    private string? _urlOverride;
    [System.Text.Json.Serialization.JsonPropertyName("url")]
    public string Url 
    { 
        get => _urlOverride ?? $"rtsp://{IpAddress}:{Port}{Path ?? "/"}";
        set => _urlOverride = value;
    }

    private string? _snapshotUrl;
    [System.Text.Json.Serialization.JsonPropertyName("snapshotUrl")]
    public string SnapshotUrl 
    { 
        get => _snapshotUrl ?? $"/api/snapshot/{Id}";
        set => _snapshotUrl = value;
    }
}

[ManagedSecurity.Common.Attributes.AllowMagicValues]
public static class RtspWellKnown
{
    // Common vendor-specific RTSP ports
    public static readonly int[] DefaultPorts = 
    { 
        554,   // Standard
        8554,  // Common Alt
        8888,  // Foscam / Alt
        5554,  // Alt
        7070,  // RealPlayer/Legacy
        10554, // Common for some Chinese OEMS
        37777, // Dahua Default (some versions proxy RTSP here)
        8000   // Hikvision Default (some versions proxy RTSP here)
    };

    public static readonly string[] CommonPaths = 
    {
        "/stream1",              // Modern ONVIF Generic / Milesight
        "/stream2",              // Modern ONVIF Generic / Milesight
        "/live/ch0",             // Generic
        "/Streaming/Channels/101", // Hikvision Main
        "/Streaming/Channels/102", // Hikvision Sub
        "/Streaming/Channels/1",   // Hikvision Simpler
        "/cam/realmonitor?channel=1&subtype=0", // Dahua Main
        "/cam/realmonitor?channel=1&subtype=1", // Dahua Sub
        "/axis-media/media.amp", // Axis
        "/axis-media/media.amp?streamprofile=Quality", // Axis High
        "/live",
        "/",
        "/video",
        "/h264",
        "/live.sdp",
        "/onvif/device_service",
        "/media/video1",          // Panasonic/Sony
        "/onvif-media/media.amp"
    };
}

