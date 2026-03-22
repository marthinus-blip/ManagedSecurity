using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedSecurity.Orchestration;

/// <summary>
/// Defines a pluggable capability for a Sentinel Agent.
/// </summary>
public interface IAgentBehavior
{
    string Name { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
}

/// <summary>
/// The primary host for distributed logic. 
/// Roles are added via composition, allowing a Scout to be promoted to Commander.
/// </summary>
[ManagedSecurity.Common.Attributes.AllowMagicValues]
public class SentinelAgent
{
    private static readonly Microsoft.Extensions.Logging.ILogger _logger = ManagedSecurity.Common.Logging.SentinelLogger.CreateLogger<SentinelAgent>();
    public string Id { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
    private readonly ConcurrentDictionary<string, IAgentBehavior> _behaviors = new();
    private readonly CancellationTokenSource _cts = new();

    public IEnumerable<string> ActiveRoles => _behaviors.Keys;

    public Task AddBehaviorAsync(IAgentBehavior behavior)
    {
        if (_behaviors.TryAdd(behavior.Name, behavior))
        {
            ManagedSecurity.Common.Logging.SentinelLogger.Info(_logger, $"[AGENT:{Id}] Activating behavior: {behavior.Name}");
            // Start the behavior in the background
            _ = behavior.StartAsync(_cts.Token);
        }
        return Task.CompletedTask;
    }

    public async Task RemoveBehaviorAsync(string name)
    {
        if (_behaviors.TryRemove(name, out var behavior))
        {
            ManagedSecurity.Common.Logging.SentinelLogger.Info(_logger, $"[AGENT:{Id}] Deactivating behavior: {name}");
            await behavior.StopAsync();
        }
    }

    public async Task ShutdownAsync()
    {
        _cts.Cancel();
        foreach (var behavior in _behaviors.Values)
        {
            await behavior.StopAsync();
        }
        _behaviors.Clear();
    }
}
