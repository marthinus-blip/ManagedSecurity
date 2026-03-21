using System;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// Defines the physical Execution boundaries mapping the telemetry of live Swarm Agents natively.
/// Architected with the explicit Ql/Rl boundary nomenclature ensuring SQLite executing engines
/// remain structurally isolated from C# Runtime memory layouts efficiently safely completely natively.
/// </summary>
public record struct AgentStateRecordRl
{
    // --- Query Language (Ql) SQLite Constants ---
    public const string TableNameQl = "AgentStateRecord";
    
    public const string AgentIdQl = "AgentId";
    public const string StatusDescriptionQl = "StatusDescription";
    public const string CpuLoadPercentageQl = "CpuLoadPercentage";
    public const string MemoryUsageBytesQl = "MemoryUsageBytes";
    public const string LastHeartbeatEpochQl = "LastHeartbeatEpoch";

    // --- Runtime Language (Rl) NativeAOT Properties ---
    public string AgentIdRl { get; init; }
    public string StatusDescriptionRl { get; init; }
    public float CpuLoadPercentageRl { get; init; }
    public long MemoryUsageBytesRl { get; init; }
    public long LastHeartbeatEpochRl { get; init; }

    /// <summary>
    /// Utility function validating active heartbeat constraints relative to network boundaries natively.
    /// </summary>
    public readonly bool IsOfficiallyDead(long currentUnixEpochRl, int timeoutSecondsRl)
    {
        return (currentUnixEpochRl - LastHeartbeatEpochRl) > timeoutSecondsRl;
    }
}
