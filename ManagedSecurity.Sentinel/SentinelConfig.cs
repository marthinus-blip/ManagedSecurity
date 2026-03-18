using System;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

namespace ManagedSecurity.Sentinel;

public class SentinelConfig
{
    [JsonPropertyName("log_level")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    [JsonPropertyName("vault_location")]
    public string VaultLocation { get; set; } = "Vault";

    [JsonPropertyName("enable_vault_recording")]
    public bool EnableVaultRecording { get; set; } = false;

    [JsonPropertyName("governor_port")]
    public int GovernorPort { get; set; } = 5188;

    [JsonPropertyName("governor_host")]
    public string GovernorHost { get; set; } = "localhost";

    [JsonPropertyName("governor_protocol")]
    public string GovernorProtocol { get; set; } = "http";

    [JsonPropertyName("storage_quota_gb")]
    public double StorageQuotaGb { get; set; } = 10.0;
}
