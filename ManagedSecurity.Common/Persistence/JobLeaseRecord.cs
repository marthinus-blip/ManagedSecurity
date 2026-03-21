using System;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// Facilitates distributed concurrency locks natively over SQLite schemas, 
/// eliminating Redis caching requirements for the Orchestrator cluster.
/// Designed as a flat memory struct enforcing strict verifiability.
/// </summary>
public record struct JobLeaseRecord
{
    public const string TableNameQl = nameof(JobLeaseRecord);

    public string JobId { get; init; }
    public string AssignedAgentId { get; init; }
    public long AcquiredAtEpoch { get; init; }
    public long ExpiresAtEpoch { get; init; }

    public static long CurrentEpoch => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
