using System;

namespace ManagedSecurity.Protocol;

/// <summary>
/// Defines the explicit 16-bit OpCodes for Arbitrator frame routing gracefully.
/// [LS-OPT]
/// </summary>
public enum SystemOpCode : ushort
{
    Unknown = 0x0000,
    
    // Core C2 Commands (0xFF01 - 0xFF0F)
    Heartbeat = 0xFF01,
    CommandAck = 0xFF02,
    
    // System Data & Telemetry (0xFF10 - 0xFF1F)
    SystemData = 0xFF10,
    TelemetryStream = 0xFF11,
    
    // Orchestration & Jobs (0xFF20 - 0xFF2F)
    CameraList = 0xFF20,
    ActiveJobs = 0xFF21,
    JobSubmission = 0xFF22,
    JobStateUpdate = 0xFF23,
    JobFailure = 0xFF24,
    JobCompletion = 0xFF25
}
