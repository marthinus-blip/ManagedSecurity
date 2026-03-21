using System;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// A flattened, NativeAOT-optimized scalar struct representing a managed camera and its orchestrational state.
/// Designed for explicit mapping via ADO.NET DbDataReader without Entity Framework Core reflection bloat.
/// </summary>
public struct CameraRecord
{
    public string CameraId { get; set; }
    public string DisplayName { get; set; }
    public string StreamUrl { get; set; }
    public string SnapshotUrl { get; set; }
    public string Vendor { get; set; }
    public string Model { get; set; }
    public string IpAddress { get; set; }
    public int Port { get; set; }
    
    /// <summary>
    /// Governs whether the Edge Scout agent should perform continuous visual analysis 
    /// (or simple HTTP polling) instead of routing purely through blind transmission.
    /// Maps to MachineVisionRoute enum (storing as byte for zero-allocation performance).
    /// </summary>
    public byte MachineVisionRoute { get; set; }

    /// <summary>
    /// AES-GCM Encrypted credential block covering RTSP passwords, access tokens, or TLS keys.
    /// Utilizes Column-Level application encryption prior to ADO.NET execution.
    /// </summary>
    public byte[] EncryptedVaultCredentials { get; set; }
    
    /// <summary>
    /// The unique cryptographic nonce/Initialization Vector used during the AES-GCM 
    /// encryption of the VaultCredentials for this specific row.
    /// </summary>
    public byte[] SecurityNonce { get; set; }

    /// <summary>
    /// Specifies if an agent has actively leased this camera's telemetry process, 
    /// determining exactly-once execution lock states.
    /// </summary>
    public bool IsOrchestrationLeased { get; set; }

    public long TenantId { get; set; }
    public long CreatedAtEpoch { get; set; }
    public long UpdatedAtEpoch { get; set; }
    public long UpdatedByUserId { get; set; }
}
