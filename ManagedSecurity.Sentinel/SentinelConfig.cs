using System;
using System.Text.Json.Serialization;

namespace ManagedSecurity.Sentinel;

public class SentinelConfig
{
    [JsonPropertyName("log_level")]
    public string LogLevel { get; set; } = "Information";

    [JsonPropertyName("vault_location")]
    public string VaultLocation { get; set; } = "Vault";

    [JsonPropertyName("enable_vault_recording")]
    public bool EnableVaultRecording { get; set; } = false;

    [JsonPropertyName("governor_port")]
    public int GovernorPort { get; set; } = 5188;

    [JsonPropertyName("storage_quota_gb")]
    public double StorageQuotaGb { get; set; } = 10.0;
}
