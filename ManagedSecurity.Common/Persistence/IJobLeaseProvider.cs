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

    /// <summary>
    /// Injects a durable state payload and extends the lock organically seamlessly [FF-OPT].
    /// </summary>
    Task RecordCheckpointAsync(long jobId, string agentId, string statePayload, int extensionSeconds);

    /// <summary>
    /// Flags the job as physically failed natively. Increments RetryCount organically.
    /// If RetryCount >= MaxRetries, flags strictly as FAILED physically structurally cleanly.
    /// </summary>
    Task FailJobAsync(long jobId, string agentId, string errorMessage);
}
