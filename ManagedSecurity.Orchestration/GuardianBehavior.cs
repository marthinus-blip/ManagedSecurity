using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ManagedSecurity.Discovery;

namespace ManagedSecurity.Orchestration;

public class GuardianBehavior : IAgentBehavior
{
    public string Name => "Guardian";
    private readonly string _agentId;
    private readonly OrchestrationConfig _config;
    private readonly Action<HeartbeatMessage>? _onHeartbeat;
    private readonly ConcurrentDictionary<string, DiscoveryResult> _activeTasks = new();
    private bool _isRunning;

    public GuardianBehavior(string agentId, OrchestrationConfig config, Action<HeartbeatMessage>? onHeartbeat = null)
    {
        _agentId = agentId;
        _config = config;
        _onHeartbeat = onHeartbeat;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _isRunning = true;
        Console.WriteLine($"[GUARDIAN] Scout {_agentId} started. Frequency: {_config.BroadPhaseInterval.TotalSeconds}s");

        // Start Heartbeat loop
        _ = HeartbeatLoop(ct);

        while (!ct.IsCancellationRequested && _isRunning)
        {
            foreach (var task in _activeTasks.Values)
            {
                await ProcessBroadPhaseAsync(task);
            }

            await Task.Delay(_config.BroadPhaseInterval, ct);
        }
    }

    public Task StopAsync()
    {
        _isRunning = false;
        return Task.CompletedTask;
    }

    public void AcceptAssignment(TaskAssignment assignment)
    {
        var camera = new DiscoveryResult(assignment.CameraId, 554, assignment.CameraUrl);
        if (_activeTasks.TryAdd(assignment.CameraUrl, camera))
        {
            Console.WriteLine($"[GUARDIAN] Monitoring started: {assignment.CameraId} ({assignment.CameraUrl})");
        }
    }


    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isRunning)
        {
            // Simulate CPU load calculation
            float load = (float)Random.Shared.NextDouble() * 0.2f; 
            _onHeartbeat?.Invoke(new HeartbeatMessage(_agentId, DateTime.UtcNow, load));
            
            await Task.Delay(_config.HeartbeatInterval, ct);
        }
    }

    private async Task ProcessBroadPhaseAsync(DiscoveryResult camera)
    {
        // Saliency simulation
        if (Random.Shared.Next(0, 100) < 5)
        {
            Console.WriteLine($"[GUARDIAN] Activity detected: {camera.IpAddress}. Raising signal.");
        }
    }
}
