using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ManagedSecurity.Discovery;

namespace ManagedSecurity.Orchestration;



public record HeartbeatMessage(string AgentId, DateTime Timestamp, float CpuLoad, float MemoryLoad, string Platform, bool IsNativeMvAttached);

public record TaskAssignment(string CameraUrl, string Id, string IpAddress, int Port, string? Path, string? SnapshotUrl, TimeSpan LeaseDuration, MachineVisionRoute MvRoute);

public record TaskLease(string CameraUrl, DateTime ExpiresAt);

public record GuardianActivityAlert(string AgentId, string CameraUrl, DateTime Timestamp, float Intensity);


/// <summary>
/// The "Commander" (General) behavior.
/// Responsible for health monitoring and task allocation across Scouts.
/// </summary>
public class CommanderBehavior : IAgentBehavior
{
    public string Name => "Commander";
    private readonly OrchestrationConfig _config;
    public record ActiveAgent(string AgentId, DateTime LastSeen, float CpuLoad, int TaskCount, float MemoryLoad, string Platform, bool IsNativeMvAttached);

    private readonly ConcurrentDictionary<string, (DateTime Timestamp, float CpuLoad, float MemoryLoad, string Platform, bool IsNativeMvAttached)> _activeWorkers = new();
    private readonly ConcurrentDictionary<string, DiscoveryResult> _unassignedCameras = new();
    private readonly ConcurrentDictionary<string, List<TaskLease>> _workerTasks = new();
    private readonly CameraStore? _store;
    private bool _isRunning;
    private string? _discoverySubnet;
    private DateTime _lastScanTime = DateTime.MinValue;
    private readonly TimeSpan _scanInterval = TimeSpan.FromMinutes(2);
    
    public event Action<string, TaskAssignment>? OnTaskAssigned;
    public event Action<GuardianActivityAlert>? OnActivityDetected;

    public CommanderBehavior(OrchestrationConfig config, CameraStore? store = null)
    {
        _config = config;
        _store = store;
    }

    public async Task InitializeFromStoreAsync()
    {
        if (_store != null)
        {
            var cameras = await _store.LoadAsync();
            foreach (var cam in cameras)
            {
                _unassignedCameras.TryAdd(cam.Url, cam);
            }
            Console.WriteLine($"[COMMANDER] Loaded {cameras.Count} cameras from store.");
        }
    }

    public void AddCameraToPool(DiscoveryResult camera)
    {
        // Use URL as unique key for now
        if (_unassignedCameras.TryAdd(camera.Url, camera))
        {
            Console.WriteLine($"[COMMANDER] Camera pooled: {camera.IpAddress}");
            _ = SaveStoreAsync();
        }
    }

    public void ConfigureCamera(string urlOrId, string displayName)
    {
        Console.WriteLine($"[COMMANDER] Attempting to configure: '{urlOrId}' as '{displayName}'");
        
        string? targetUrl = null;
        if (_unassignedCameras.ContainsKey(urlOrId))
        {
            targetUrl = urlOrId;
        }
        else 
        {
            // Try searching by ID
            var match = _unassignedCameras.Values.FirstOrDefault(c => c.Id == urlOrId || c.Url == urlOrId);
            if (match != null)
            {
                targetUrl = match.Url;
            }
        }

        if (targetUrl != null)
        {
            var cam = _unassignedCameras[targetUrl];
            cam.DisplayName = displayName;
            cam.IsConfigured = true;
            Console.WriteLine($"[COMMANDER] Configured {targetUrl} as '{displayName}'");
            _ = SaveStoreAsync();
        }
        else 
        {
            Console.WriteLine($"[COMMANDER] Warning: Failed to find camera for configuration. Input was: '{urlOrId}'");
            Console.WriteLine($"[COMMANDER] Available keys: {string.Join(", ", _unassignedCameras.Keys)}");
        }
    }

    private async Task SaveStoreAsync()
    {
        if (_store != null)
        {
            try 
            {
                await _store.SaveAsync(_unassignedCameras.Values.ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[COMMANDER] CRITICAL: Failed to save camera store: {ex.Message}");
            }
        }
    }

    public void EnableAutoDiscovery(string subnet)
    {
        _discoverySubnet = subnet;
        Console.WriteLine($"[COMMANDER] Auto-Discovery enabled for {subnet}.0/24");
    }

    private async Task PerformDiscoveryScanAsync()
    {
        if (string.IsNullOrEmpty(_discoverySubnet)) return;
        if (DateTime.UtcNow - _lastScanTime < _scanInterval) return;

        _lastScanTime = DateTime.UtcNow;
        Console.WriteLine($"[COMMANDER] Background scan started on {_discoverySubnet}.0/24...");
        
        try 
        {
            var scanner = new RtspScanner();
            var results = await scanner.ScanSubnetAsync(_discoverySubnet);
            
            var onvif = new OnvifDiscovery();
            var onvifDevices = await onvif.ProbeAsync();

            foreach (var r in results)
            {
                var match = onvifDevices.FirstOrDefault(o => o.IpAddress == r.IpAddress);
                var finalResult = r;
                if (match != null)
                {
                    finalResult.Vendor = match.Name;
                    finalResult.Model = match.Model;
                }
                AddCameraToPool(finalResult);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[COMMANDER] Discovery scan failed: {ex.Message}");
        }
    }

    public List<DiscoveryResult> GetCameras() => _unassignedCameras.Values.ToList();

    public void TriggerManualScan()
    {
        _lastScanTime = DateTime.MinValue;
        _ = PerformDiscoveryScanAsync();
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _isRunning = true;
        Console.WriteLine($"[COMMANDER] General initialized. Governance interval: {_config.GovernanceInterval.TotalSeconds}s");
        
        while (!ct.IsCancellationRequested && _isRunning)
        {
            await Task.Delay(_config.GovernanceInterval, ct);
            PerformGovernance();
            AssignPendingTasks();

            if (!string.IsNullOrEmpty(_discoverySubnet))
            {
                _ = PerformDiscoveryScanAsync();
            }
        }
    }

    public Task StopAsync()
    {
        _isRunning = false;
        return Task.CompletedTask;
    }

    public void ReceiveHeartbeat(HeartbeatMessage hb)
    {
        _activeWorkers.AddOrUpdate(hb.AgentId, 
            (hb.Timestamp, hb.CpuLoad, hb.MemoryLoad, hb.Platform, hb.IsNativeMvAttached), 
            (_, _) => (hb.Timestamp, hb.CpuLoad, hb.MemoryLoad, hb.Platform, hb.IsNativeMvAttached));
    }

    public IEnumerable<ActiveAgent> GetActiveAgents()
    {
        return _activeWorkers.Select(kv => new ActiveAgent(
            kv.Key, 
            kv.Value.Timestamp, 
            kv.Value.CpuLoad, 
            _workerTasks.TryGetValue(kv.Key, out var tasks) ? tasks.Count : 0,
            kv.Value.MemoryLoad,
            kv.Value.Platform,
            kv.Value.IsNativeMvAttached
        ));
    }

    public void ReceiveAlert(GuardianActivityAlert alert)
    {
        Console.WriteLine($"[COMMANDER] ALERT from Scout {alert.AgentId}: Interest level {alert.Intensity:P1} on {alert.CameraUrl}");
        OnActivityDetected?.Invoke(alert);
    }

    private void PerformGovernance()
    {
        var now = DateTime.UtcNow;
        var deadWorkers = _activeWorkers
            .Where(w => now - w.Value.Timestamp > _config.WorkerTimeout)
            .Select(w => w.Key)
            .ToList();

        foreach (var id in deadWorkers)
        {
            if (_activeWorkers.TryRemove(id, out _))
            {
                Console.WriteLine($"[COMMANDER] Scout {id} is MIA. Salvaging eyes...");
                // Re-pool tasks from this worker
                if (_workerTasks.TryRemove(id, out var tasks))
                {
                    foreach (var t in tasks) 
                    {
                        Console.WriteLine($"[COMMANDER] Tasks {t.CameraUrl} back in pool.");
                        // In reality we'd need to reconstruct the DiscoveryResult
                    }
                }
            }
        }
    }

    private void AssignPendingTasks()
    {
        if (_activeWorkers.IsEmpty) return;

        foreach (var camera in _unassignedCameras.Values)
        {
            // Check if already assigned
            if (_workerTasks.Values.Any(tl => tl.Any(l => l.CameraUrl == camera.Url))) continue;

            // Simple Round-Robin or Least-Load assignment
            var targetWorker = _activeWorkers.Keys.First(); // For now just the first one
            
            var assignment = new TaskAssignment(camera.Url, camera.Id, camera.IpAddress, camera.Port, camera.Path, camera.SnapshotUrl, TimeSpan.FromMinutes(5), camera.MvRoute);
            var lease = new TaskLease(camera.Url, DateTime.UtcNow.Add(assignment.LeaseDuration));

            _workerTasks.AddOrUpdate(targetWorker, 
                new List<TaskLease> { lease }, 
                (_, list) => { list.Add(lease); return list; });

            Console.WriteLine($"[COMMANDER] Assigned {camera.IpAddress} to Scout {targetWorker}");
            OnTaskAssigned?.Invoke(targetWorker, assignment);
        }
    }
}

