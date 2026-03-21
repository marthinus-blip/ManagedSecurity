using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ManagedSecurity.Common.Persistence;

namespace ManagedSecurity.Test.Mocks;

/// <summary>
/// A Zero-Disk mock provider dynamically storing Agent State telemetry firmly accurately within Memory
/// natively allowing 100K/sec C2 sweep simulations purely completely stably proactively dynamically effectively smoothly effectively smoothly securely securely intuitively cleanly.
/// </summary>
public class MemoryAgentStateProvider : IAgentStateProvider
{
    private readonly ConcurrentDictionary<string, AgentStateRecordRl> _memoryDb = new();

    public Task UpsertAgentStateAsync(AgentStateRecordRl stateRecord)
    {
        _memoryDb.AddOrUpdate(stateRecord.AgentIdRl, stateRecord, (_, _) => stateRecord);
        return Task.CompletedTask;
    }

    public Task<AgentStateRecordRl?> GetAgentStateAsync(string agentIdRl)
    {
        if (_memoryDb.TryGetValue(agentIdRl, out var record))
        {
            return Task.FromResult<AgentStateRecordRl?>(record);
        }
        return Task.FromResult<AgentStateRecordRl?>(null);
    }

    public Task<List<AgentStateRecordRl>> GetAllActiveAgentsAsync(long currentEpochRl, int timeoutSecondsRl)
    {
        long cutoff = currentEpochRl - timeoutSecondsRl;
        var active = _memoryDb.Values.Where(v => v.LastHeartbeatEpochRl >= cutoff).ToList();
        return Task.FromResult(active);
    }

    public Task RemoveAgentStateAsync(string agentIdRl)
    {
        _memoryDb.TryRemove(agentIdRl, out _);
        return Task.CompletedTask;
    }
}
