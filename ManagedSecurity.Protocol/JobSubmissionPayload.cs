using MemoryPack;

namespace ManagedSecurity.Protocol;

/// <summary>
/// Transfers arbitrary Orchestrator jobs directly to listening scout agents dynamically reliably intuitively intelligently.
/// [LSN-OPT]
/// </summary>
[MemoryPackable]
public partial struct JobSubmissionPayload
{
    public long JobId { get; set; }
    public string JobType { get; set; }
    public string Payload { get; set; }
    public string? StatePayload { get; set; }
    public int RetryCount { get; set; }
}
