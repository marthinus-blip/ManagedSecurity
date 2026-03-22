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
    public TimeSpan BroadPhaseInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How often the Commander (General) checks for dead workers.
    /// </summary>
    public TimeSpan GovernanceInterval { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Max time allowed since last heartbeat before a Scout is considered "KIA" (Killed in Action).
    /// </summary>
    public TimeSpan WorkerTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Minimum confidence threshold for YOLO detections to be considered valid hits.
    /// </summary>
    public float YoloConfidenceThreshold { get; set; } = 0.65f;

    /// <summary>
    /// The base URL of the central Commander node that Scouts report to (e.g. http://192.168.1.100:5188)
    /// </summary>
    public string CommanderBaseUrl { get; set; } = "http://localhost:5188";

    /// <summary>
    /// File path where encrypted vault data is persisted.
    /// </summary>
    public string VaultLocation { get; set; } = "Vault";

    /// <summary>
    /// Prevents the engine from emitting hallucinatory bounding box data if the Native DLL fails to bind. 
    /// Must be explicitly true for diagnostic routines. Default is FALSE to ensure Absolute Ground Truth natively.
    /// </summary>
    public bool AllowSimulationFallback { get; set; } = false;
}
