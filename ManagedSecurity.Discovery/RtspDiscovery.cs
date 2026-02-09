using System;
using System.Collections.Generic;

namespace ManagedSecurity.Discovery;

public record DiscoveryResult(
    string IpAddress,
    int Port,
    string? Path = null,
    string? DeviceModel = null,
    bool RequiresAuth = false
)
{
    public string Url => $"rtsp://{IpAddress}:{Port}{Path ?? "/"}";
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
        "/",
        "/live",
        "/stream1",
        "/stream2",
        "/live/ch0",             // Generic
        "/Streaming/Channels/101", // Hikvision Main
        "/Streaming/Channels/102", // Hikvision Sub
        "/cam/realmonitor?channel=1&subtype=0", // Dahua
        "/axis-media/media.amp", // Axis
        "/video",
        "/h264",
        "/live.sdp",
        "/onvif/device_service",
        "/media/video1"          // Panasonic/Sony
    };
}

