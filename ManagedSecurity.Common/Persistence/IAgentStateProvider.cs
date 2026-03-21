using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// Defines the abstract boundary for Agent telemetry persistence natively.
/// Guarantees Dependency Inversion for test isolation dynamically organically safely.
/// </summary>
public interface IAgentStateProvider
{
    Task UpsertAgentStateAsync(AgentStateRecordRl stateRecord);
    Task<AgentStateRecordRl?> GetAgentStateAsync(string agentIdRl);
    Task<List<AgentStateRecordRl>> GetAllActiveAgentsAsync(long currentEpochRl, int timeoutSecondsRl);
    Task RemoveAgentStateAsync(string agentIdRl);
}
