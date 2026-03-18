using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ManagedSecurity.Core;
using ManagedSecurity.Discovery;
using ManagedSecurity.Orchestration.Engine;

namespace ManagedSecurity.Orchestration;

public class GuardianBehavior : IAgentBehavior
{
    public string Name => "Guardian";
    private readonly string _agentId;
    private readonly OrchestrationConfig _config;
    private readonly Action<HeartbeatMessage>? _onHeartbeat;
    private readonly Action<GuardianActivityAlert>? _onAlert;
    private readonly Cipher? _cipher;
    private readonly ConcurrentDictionary<string, DiscoveryResult> _activeTasks = new();
    private readonly ConcurrentDictionary<string, byte[]> _lastFrames = new();
    
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _feedCts = new();
    private readonly ConcurrentDictionary<string, Task> _feedLoops = new();

    private readonly Func<bool>? _isNativeProvider;
    private readonly Func<string>? _engineVersionProvider;
    private bool _isRunning;
    private bool _firstFrameSaved = false;

    public GuardianBehavior(
        string agentId, 
        OrchestrationConfig config, 
        Action<HeartbeatMessage>? onHeartbeat = null,
        Action<GuardianActivityAlert>? onAlert = null,
        Cipher? cipher = null,
        Func<bool>? isNativeProvider = null,
        Func<string>? engineVersionProvider = null)
    {
        _agentId = agentId;
        _config = config;
        _onHeartbeat = onHeartbeat;
        _onAlert = onAlert;
        _cipher = cipher;
        _isNativeProvider = isNativeProvider;
        _engineVersionProvider = engineVersionProvider;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _isRunning = true;
        Console.WriteLine($"[GUARDIAN] Scout {_agentId} started. Frequency: {_config.BroadPhaseInterval.TotalSeconds}s");

        // Start Heartbeat loop
        _ = HeartbeatLoop(ct);

        // Core execution loop is now decoupled into per-camera tasks via AcceptAssignment
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _isRunning = false;
        foreach (var feedCts in _feedCts.Values)
        {
            feedCts.Cancel();
        }
        return Task.CompletedTask;
    }

    public void AcceptAssignment(TaskAssignment assignment)
    {
        var camera = new DiscoveryResult(assignment.IpAddress, assignment.Port, assignment.Path)
        {
            Id = assignment.Id,
            SnapshotUrl = assignment.SnapshotUrl ?? $"/api/snapshot/{assignment.Id}",
            MvRoute = assignment.MvRoute
        };
        
        if (camera.SnapshotUrl.StartsWith("/"))
        {
            camera.SnapshotUrl = _config.CommanderBaseUrl.TrimEnd('/') + camera.SnapshotUrl;
        }

        if (_activeTasks.TryAdd(assignment.CameraUrl, camera))
        {
            Console.WriteLine($"[GUARDIAN] Monitoring started: {assignment.Id} ({assignment.IpAddress}) via {assignment.MvRoute}");
            
            // Map the appropriate feed strategy dynamically
            IMachineVisionFeedStrategy feedStrategy;
            if (assignment.MvRoute == MachineVisionRoute.LightPlain)
            {
                feedStrategy = new PollingSnapshotFeedStrategy(camera, _config.BroadPhaseInterval, _cipher);
            }
            else 
            {
                // Placeholder for Heavy routes (DecryptedStreamFeedStrategy)
                feedStrategy = new PollingSnapshotFeedStrategy(camera, _config.BroadPhaseInterval, _cipher);
            }

            var cts = new CancellationTokenSource();
            _feedCts[assignment.CameraUrl] = cts;
            _feedLoops[assignment.CameraUrl] = Task.Run(() => BroadPhaseLoopAsync(camera, feedStrategy, cts.Token), cts.Token);
        }
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isRunning)
        {
            // Calculate a synthetic load for the General to see
            float load = (float)(_activeTasks.Count * 0.05); // 5% per active camera
            float memoryMb = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024f * 1024f);
            string platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
            bool isNative = _isNativeProvider?.Invoke() ?? false;
            string engineVersion = _engineVersionProvider?.Invoke() ?? string.Empty;
            
            _onHeartbeat?.Invoke(new HeartbeatMessage(_agentId, DateTime.UtcNow, load, memoryMb, platform, isNative, engineVersion));
            
            await Task.Delay(_config.HeartbeatInterval, ct);
        }
    }

    private async Task BroadPhaseLoopAsync(DiscoveryResult camera, IMachineVisionFeedStrategy feedStrategy, CancellationToken ct)
    {
        try 
        {
            while (!ct.IsCancellationRequested && _isRunning)
            {
                using var frame = await feedStrategy.GetNextFrameAsync(ct);
                if (frame == null) continue;

                var rawData = frame.Data.ToArray();
                byte[] currentFrame = rawData;

                if (!_firstFrameSaved)
                {
                    _firstFrameSaved = true;
                    try 
                    {
                        var path = $"/tmp/guardian_first_frame_{camera.Id}.jpg";
                        await System.IO.File.WriteAllBytesAsync(path, currentFrame, ct);
                        Console.WriteLine($"[GUARDIAN] GROUND TRUTH VERIFICATION: Saved {currentFrame.Length} bytes to {path}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GUARDIAN] Failed to save verification frame: {ex.Message}");
                    }
                }

                if (_cipher != null && camera.MvRoute == MachineVisionRoute.LightPlain == false) // Simple check
                {
                    try 
                    {
                        currentFrame = _cipher.Decrypt(rawData);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GUARDIAN] Decryption failed for {camera.IpAddress}: {ex.Message}");
                        continue;
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
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[GUARDIAN] Feed loop crashed for {camera.IpAddress}: {ex.Message}");
        }
        finally 
        {
            if (feedStrategy is IDisposable d) d.Dispose();
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
