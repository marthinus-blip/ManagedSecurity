using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ManagedSecurity.Discovery;

namespace ManagedSecurity.Orchestration;



public record HeartbeatMessage(string AgentId, DateTime Timestamp, float CpuLoad);

public record TaskAssignment(string CameraUrl, string CameraId, TimeSpan LeaseDuration);

public record TaskLease(string CameraUrl, DateTime ExpiresAt);


/// <summary>
/// The "Commander" (General) behavior.
/// Responsible for health monitoring and task allocation across Scouts.
/// </summary>
public class CommanderBehavior : IAgentBehavior
{
    public string Name => "Commander";
    private readonly OrchestrationConfig _config;
    private readonly ConcurrentDictionary<string, DateTime> _activeWorkers = new();
    private readonly ConcurrentQueue<DiscoveryResult> _unassignedCameras = new();
    private readonly ConcurrentDictionary<string, List<TaskLease>> _workerTasks = new();
    private bool _isRunning;
    
    public event Action<string, TaskAssignment>? OnTaskAssigned;

    public CommanderBehavior(OrchestrationConfig config)
    {
        _config = config;
    }

    public void AddCameraToPool(DiscoveryResult camera)
    {
        _unassignedCameras.Enqueue(camera);
        Console.WriteLine($"[COMMANDER] Camera pooled: {camera.IpAddress}");
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
        }
    }

    public Task StopAsync()
    {
        _isRunning = false;
        return Task.CompletedTask;
    }

    public void ReceiveHeartbeat(HeartbeatMessage hb)
    {
        _activeWorkers.AddOrUpdate(hb.AgentId, hb.Timestamp, (_, _) => hb.Timestamp);
    }

    private void PerformGovernance()
    {
        var now = DateTime.UtcNow;
        var deadWorkers = _activeWorkers
            .Where(w => now - w.Value > _config.WorkerTimeout)
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

        while (_unassignedCameras.TryDequeue(out var camera))
        {
            // Simple Round-Robin or Least-Load assignment
            var targetWorker = _activeWorkers.Keys.First(); // For now just the first one
            
            var assignment = new TaskAssignment(camera.Url, camera.IpAddress, TimeSpan.FromMinutes(5));
            var lease = new TaskLease(camera.Url, DateTime.UtcNow.Add(assignment.LeaseDuration));

            _workerTasks.AddOrUpdate(targetWorker, 
                new List<TaskLease> { lease }, 
                (_, list) => { list.Add(lease); return list; });

            Console.WriteLine($"[COMMANDER] Assigned {camera.IpAddress} to Scout {targetWorker}");
            OnTaskAssigned?.Invoke(targetWorker, assignment);
        }
    }
}

