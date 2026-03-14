using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ManagedSecurity.Core;
using ManagedSecurity.Discovery;

namespace ManagedSecurity.Orchestration;

public class GuardianBehavior : IAgentBehavior
{
    public string Name => "Guardian";
    private readonly string _agentId;
    private readonly OrchestrationConfig _config;
    private readonly Action<HeartbeatMessage>? _onHeartbeat;
    private readonly Action<GuardianActivityAlert>? _onAlert;
    private readonly string _hubBaseUrl;
    private readonly Cipher? _cipher;
    private readonly ConcurrentDictionary<string, DiscoveryResult> _activeTasks = new();
    private readonly ConcurrentDictionary<string, byte[]> _lastFrames = new();
    private readonly HttpClient _http = new();
    private bool _isRunning;

    public GuardianBehavior(
        string agentId, 
        OrchestrationConfig config, 
        string hubBaseUrl = "http://localhost:5188",
        Action<HeartbeatMessage>? onHeartbeat = null,
        Action<GuardianActivityAlert>? onAlert = null,
        Cipher? cipher = null)
    {
        _agentId = agentId;
        _config = config;
        _hubBaseUrl = hubBaseUrl.TrimEnd('/');
        _onHeartbeat = onHeartbeat;
        _onAlert = onAlert;
        _cipher = cipher;
        
        // Timeout snapshots longer to allow GStreamer startup
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _isRunning = true;
        Console.WriteLine($"[GUARDIAN] Scout {_agentId} started. Frequency: {_config.BroadPhaseInterval.TotalSeconds}s");

        // Start Heartbeat loop
        _ = HeartbeatLoop(ct);

        while (!ct.IsCancellationRequested && _isRunning)
        {
            var tasks = _activeTasks.Values.ToList();
            foreach (var task in tasks)
            {
                try 
                {
                    await ProcessBroadPhaseAsync(task, ct);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GUARDIAN] Task yield failure for {task.IpAddress}: {ex.Message}");
                }
            }

            await Task.Delay(_config.BroadPhaseInterval, ct);
        }
    }

    public Task StopAsync()
    {
        _isRunning = false;
        _http.Dispose();
        return Task.CompletedTask;
    }

    public void AcceptAssignment(TaskAssignment assignment)
    {
        var camera = new DiscoveryResult(assignment.IpAddress, assignment.Port, assignment.Path)
        {
            Id = assignment.Id,
            SnapshotUrl = assignment.SnapshotUrl ?? $"/api/snapshot/{assignment.Id}"
        };
        if (_activeTasks.TryAdd(assignment.CameraUrl, camera))
        {
            Console.WriteLine($"[GUARDIAN] Monitoring started: {assignment.Id} ({assignment.IpAddress})");
        }
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isRunning)
        {
            // Calculate a synthetic load for the General to see
            float load = (float)(_activeTasks.Count * 0.05); // 5% per active camera
            _onHeartbeat?.Invoke(new HeartbeatMessage(_agentId, DateTime.UtcNow, load));
            
            await Task.Delay(_config.HeartbeatInterval, ct);
        }
    }

    private async Task ProcessBroadPhaseAsync(DiscoveryResult camera, CancellationToken ct)
    {
        // 1. Fetch Snapshot (HTTP JPEG)
        // Note: Many cameras provide a static snapshot URL (e.g. /isapi/streaming/channels/101/picture)
        // For now we assume the SnapshotUrl is provided or derived.
        string snapshotUrl = camera.SnapshotUrl;
        if (string.IsNullOrEmpty(snapshotUrl)) return;
        
        if (snapshotUrl.StartsWith("/"))
        {
            snapshotUrl = _hubBaseUrl + snapshotUrl;
        }

        Console.WriteLine($"[GUARDIAN] Polling snapshot for {camera.IpAddress}: {snapshotUrl}");

        try 
        {
            byte[] rawData = await _http.GetByteArrayAsync(snapshotUrl, ct);
            byte[] currentFrame = rawData;

            if (_cipher != null)
            {
                try 
                {
                    currentFrame = _cipher.Decrypt(rawData);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GUARDIAN] Decryption failed for {camera.IpAddress}: {ex.Message}");
                    return;
                }
            }
            
            if (_lastFrames.TryGetValue(camera.Url, out byte[]? lastFrame))
            {
                // 2. Compute Saliency (Placeholder: Basic byte-diff intensity)
                float intensity = ComputeSaliency(lastFrame, currentFrame);
                
                if (intensity > 0.05f) // 5% change threshold
                {
                    _onAlert?.Invoke(new GuardianActivityAlert(_agentId, camera.Url, DateTime.UtcNow, intensity));
                }
            }

            _lastFrames[camera.Url] = currentFrame;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GUARDIAN] Snap poll failed for {camera.IpAddress}: {ex.Message}");
        }
    }

    private float ComputeSaliency(byte[] last, byte[] current)
    {
        if (last.Length != current.Length) return 1.0f; // Significant structure change

        // extremely basic byte comparison for the manifold demo
        // In production, this would be a TFLite model or OpenCV background-subtraction
        long diff = 0;
        int step = Math.Max(1, last.Length / 1000); // Sample 1000 points
        int samples = 0;

        for (int i = 0; i < last.Length; i += step)
        {
            diff += Math.Abs(last[i] - current[i]);
            samples++;
        }

        return (float)diff / (samples * 255);
    }
}
