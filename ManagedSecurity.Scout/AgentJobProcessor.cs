using System;
using System.Threading;
using System.Threading.Tasks;
using ManagedSecurity.Protocol;

namespace ManagedSecurity.Scout;

/// <summary>
/// A base abstraction for long-running Temporal jobs executing locally on the Scout.
/// </summary>
public abstract class AgentJobProcessor
{
    public abstract string TargetJobType { get; }

    /// <summary>
    /// Formally evaluates the payload boundary and executes the workload natively.
    /// Returns an optional completion string payload if successful.
    /// </summary>
    public abstract Task<string?> ExecuteJobAsync(long jobId, string workloadPayload, string? genericStateContext, CancellationToken cancellationToken);

    /// <summary>
    /// Internal delegate mapping the WS Tunnel for checkpoints seamlessly.
    /// </summary>
    internal Func<long, string, int, Task>? TunnelCheckpointDispatcher { get; set; }

    public const int DefaultCheckpointExtensionSeconds = 60;

    /// <summary>
    /// Requests a lock extension and injects the durable state payload checkpoints to the database.
    /// </summary>
    protected async Task CheckpointStateAsync(long jobId, string state, int durationExtensionSeconds = DefaultCheckpointExtensionSeconds)
    {
        if (TunnelCheckpointDispatcher != null)
        {
            await TunnelCheckpointDispatcher(jobId, state, durationExtensionSeconds);
        }
    }
}
