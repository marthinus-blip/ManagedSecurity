using System;
using System.Collections.Generic;

namespace ManagedSecurity.Discovery;

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
    
    [System.Text.Json.Serialization.JsonPropertyName("url")]
    public string Url 
    { 
        get => $"rtsp://{IpAddress}:{Port}{Path ?? "/"}";
        set { /* Required for some serializers, but we mostly use getter */ }
    }

    private string? _snapshotUrl;
    [System.Text.Json.Serialization.JsonPropertyName("snapshotUrl")]
    public string SnapshotUrl 
    { 
        get => _snapshotUrl ?? $"/api/snapshot/{Id}";
        set => _snapshotUrl = value;
    }
}

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

