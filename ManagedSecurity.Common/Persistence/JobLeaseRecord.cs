using System;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// A Zero-Allocation struct physically mapping the PostgreSQL Atomic Queue table natively.
/// Ensures the Orchestrator cluster can dequeue payloads gracefully utilizing SKIP LOCKED mechanics natively.
/// </summary>
public readonly record struct JobLeaseRecord
{
    public const string SchemaNameQl = "orchestrator";
    public const string TableNameQl = "Jobs";

    public long Id { get; init; }
    public long TenantId { get; init; }
    public string JobType { get; init; }
    public string Payload { get; init; }

    public string? AssignedAgentId { get; init; }
    public long AcquiredAtEpoch { get; init; }
    public long ExpiresAtEpoch { get; init; }

    public static long CurrentEpoch => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
