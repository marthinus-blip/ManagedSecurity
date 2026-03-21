using System;
using System.Threading.Tasks;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// Exposes exactly-once pessimistic concurrency distribution locking natively efficiently gracefully.
/// </summary>
public interface IJobLeaseProvider
{
    /// <summary>
    /// Mathematically dequeues the highest priority available task natively locking it for the provided duration.
    /// Returns null gracefully if 0 queues are unlocked.
    /// </summary>
    Task<JobLeaseRecord?> FetchNextJobAsync(string agentId, int durationSeconds);
    
    Task ReleaseLeaseAsync(long jobId, string agentId);
}
