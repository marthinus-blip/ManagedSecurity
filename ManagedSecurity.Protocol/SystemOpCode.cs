using System;

namespace ManagedSecurity.Protocol;

/// <summary>
/// Defines the explicit 16-bit OpCodes for Arbitrator frame routing gracefully.
/// [LS-OPT]
/// </summary>
public enum SystemOpCode : ushort
{
    Unknown = 0x0000,
    
    // Core C2 Commands (0x3F01 - 0x3F0F)
    Heartbeat = 0x3F01,
    CommandAck = 0x3F02,
    
    // System Data & Telemetry (0x3F10 - 0x3F1F)
    SystemData = 0x3F10,
    TelemetryStream = 0x3F11,
    
    // Orchestration & Jobs (0x3F20 - 0x3F2F)
    CameraList = 0x3F20,
    ActiveJobs = 0x3F21,
    JobSubmission = 0x3F22,
    JobStateUpdate = 0x3F23,
    JobFailure = 0x3F24,
    JobCompletion = 0x3F25,
    
    // WebRTC Signaling & Reverse Routing (0x3F30 - 0x3F3F)
    P2PSignal = 0x3F30
}
