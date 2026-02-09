using System;

namespace ManagedSecurity.Orchestration;

public class OrchestrationConfig
{
    /// <summary>
    /// How often the Scout sends a heartbeat to the Commander.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often the Guardian (Scout) performs Broad-Phase analysis.
    /// </summary>
    public TimeSpan BroadPhaseInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How often the Commander (General) checks for dead workers.
    /// </summary>
    public TimeSpan GovernanceInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Max time allowed since last heartbeat before a Scout is considered "KIA" (Killed in Action).
    /// </summary>
    public TimeSpan WorkerTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
