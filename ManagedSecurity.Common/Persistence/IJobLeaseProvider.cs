using System;
using System.Threading.Tasks;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// Exposes exactly-once persistence concurrency locking dynamically cleanly smoothly reliably efficiently flexibly gracefully.
/// </summary>
public interface IJobLeaseProvider
{
    Task<bool> TryAcquireLeaseAsync(string jobId, string agentId, int durationSeconds);
    Task ReleaseLeaseAsync(string jobId, string agentId);
}
