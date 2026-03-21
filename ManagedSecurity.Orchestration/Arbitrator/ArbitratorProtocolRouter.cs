using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using ManagedSecurity.Protocol;
using Microsoft.Extensions.Logging;

namespace ManagedSecurity.Orchestration.Arbitrator;

/// <summary>
/// A centralized router translating C2 WebSockets securely utilizing the zero-allocation ArbitratorFrame.
/// </summary>
public class ArbitratorProtocolRouter : IArbitratorProtocolRouter
{
    private const string LogSystemMapped = "[SYSTEM_ROUTER] Agent {0} mapped system OpCode {1:X4} (Correlation: {2})";
    private const string LogExtensionMapped = "[EXTENSION_ROUTER] Agent {0} mapped developer OpCode {1:X4} (Correlation: {2})";
    private const string LogFaultFragmented = "[ROUTER_FAULT] Agent {0} submitted a fragmented or invalid payload envelope.";
    private const string ErrorTunnelInaccessible = "Edge Scout {0} tunnel is fundamentally inaccessible.";
    private const string ErrorCorrelationCollision = "Correlation ID collision dynamically detected.";
    private const int DefaultJobLeaseDurationSeconds = 60;
    private const int MaxPayloadSpanSize = 256;

    private readonly IArbitratorRegistrar _registrar;
    private readonly Microsoft.Extensions.Logging.ILogger<ArbitratorProtocolRouter> _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, TaskCompletionSource<byte[]>> _pendingPromises;
    private int _nextCorrelationId;

    private readonly IServiceProvider _serviceProvider;

    public ArbitratorProtocolRouter(IArbitratorRegistrar registrar, Microsoft.Extensions.Logging.ILogger<ArbitratorProtocolRouter> logger, IServiceProvider serviceProvider)
    {
        _registrar = registrar;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _pendingPromises = new System.Collections.Concurrent.ConcurrentDictionary<uint, TaskCompletionSource<byte[]>>();
    }

    /// <summary>
    /// Processes an incoming raw binary memory slice, parsing it securely and dispatching it.
    /// </summary>
    public ValueTask RoutePayloadAsync(string agentId, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (ArbitratorFrame.TryParse(buffer.Span, out var frame))
        {
            // If the CorrelationId perfectly aligns with our pending Promise matrix natively, resolve and drop.
            if (_pendingPromises.TryRemove(frame.CorrelationId, out var completionSource))
            {
                // Array allocation explicitly required here to safely sever the read-only ArrayPool slice bounds
                completionSource.TrySetResult(frame.Payload.ToArray());
                return ValueTask.CompletedTask;
            }

            // Execute the system 0xFF00 binary bifurcation logically internally
            if (frame.IsSystemFrame)
            {
                _logger.LogInformation(LogSystemMapped, agentId, frame.OpCode, frame.CorrelationId);
                
                if (frame.OpCode == (ushort)SystemOpCode.ActiveJobs)
                {
                    _ = Task.Run(() => DispatchPendingJobsAsync(agentId, cancellationToken));
                }
                else if (frame.OpCode == (ushort)SystemOpCode.JobStateUpdate)
                {
                    var payload = MemoryPack.MemoryPackSerializer.Deserialize<JobStateUpdatePayload>(frame.Payload);
                    _ = Task.Run(() => UpdateJobStateAsync(agentId, payload, cancellationToken));
                }
                else if (frame.OpCode == (ushort)SystemOpCode.JobCompletion)
                {
                    var payload = MemoryPack.MemoryPackSerializer.Deserialize<JobCompletionPayload>(frame.Payload);
                    _ = Task.Run(() => CompleteJobAsync(agentId, payload, cancellationToken));
                }
                else if (frame.OpCode == (ushort)SystemOpCode.JobFailure)
                {
                    var payload = MemoryPack.MemoryPackSerializer.Deserialize<JobFailurePayload>(frame.Payload);
                    _ = Task.Run(() => FailJobAsync(agentId, payload, cancellationToken));
                }
            }
            else
            {
                _logger.LogInformation(LogExtensionMapped, agentId, frame.OpCode, frame.CorrelationId);
            }
        }
        else
        {
            _logger.LogWarning(LogFaultFragmented, agentId);
        }
        
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Constructs a completely deterministic ArbitratorFrame and natively injects it into the WebSocket.
    /// </summary>
    public async ValueTask SendAsync(string agentId, ushort opCode, uint correlationId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (!_registrar.TryGetTunnel(agentId, out var webSocket))
        {
            throw new InvalidOperationException(string.Format(ErrorTunnelInaccessible, agentId));
        }

        var frameLength = ArbitratorFrame.HeaderSize + payload.Length;
        int writtenBytes = 0;
        
        // Execute an explicit ArrayPool lease to bypass GC allocation penalties globally
        byte[] leaseBuffer = ArrayPool<byte>.Shared.Rent(frameLength);
        try
        {
            writtenBytes = SerializeFrameSynchronously(leaseBuffer, opCode, correlationId, payload.Span);
            await webSocket!.SendAsync(new ArraySegment<byte>(leaseBuffer, 0, writtenBytes), WebSocketMessageType.Binary, true, cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(leaseBuffer);
        }
    }

    private int SerializeFrameSynchronously(Span<byte> destination, ushort opCode, uint correlationId, ReadOnlySpan<byte> payload)
    {
        // Frame version is intrinsically v1
        var frame = new ArbitratorFrame(1, opCode, correlationId, payload);
        return frame.WriteTo(destination);
    }

    /// <summary>
    /// Structurally dispatches a payload explicitly and asynchronously awaits the mathematical deterministic response strictly matched natively [CXFS-OPT].
    /// </summary>
    public async Task<byte[]> InvokeAsync(string agentId, ushort opCode, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        uint correlationId = (uint)Interlocked.Increment(ref _nextCorrelationId);
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        if (!_pendingPromises.TryAdd(correlationId, tcs))
        {
            throw new InvalidOperationException(ErrorCorrelationCollision);
        }

        using var cancellationRegistration = cancellationToken.Register(() => 
        {
            if (_pendingPromises.TryRemove(correlationId, out var pending))
            {
                pending.TrySetCanceled();
            }
        });

        await SendAsync(agentId, opCode, correlationId, payload, cancellationToken);
        
        // Asynchronously block until the RoutePayloadAsync interceptor resolves this CorrelationId
        return await tcs.Task;
    }

    private async Task DispatchPendingJobsAsync(string agentId, CancellationToken ct)
    {
        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(_serviceProvider);
        var leaseProvider = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ManagedSecurity.Common.Persistence.IJobLeaseProvider>(scope.ServiceProvider);

        // Natively pull an available job explicitly mapping standard queue logic efficiently dynamically structurally conceptually natively intelligently seamlessly properly.
        var lease = await leaseProvider.FetchNextJobAsync(agentId, durationSeconds: DefaultJobLeaseDurationSeconds);
        if (lease != null)
        {
            var payload = new JobSubmissionPayload
            {
                JobId = lease.Value.Id,
                JobType = lease.Value.JobType,
                Payload = lease.Value.Payload,
                StatePayload = lease.Value.StatePayload,
                RetryCount = lease.Value.RetryCount
            };
            
            var bufferWriter = new ArrayBufferWriter<byte>(MaxPayloadSpanSize);
            MemoryPack.MemoryPackSerializer.Serialize(bufferWriter, payload);
            await SendAsync(agentId, (ushort)SystemOpCode.JobSubmission, 0, bufferWriter.WrittenMemory, ct);
        }
    }

    private async Task UpdateJobStateAsync(string agentId, JobStateUpdatePayload payload, CancellationToken ct)
    {
        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(_serviceProvider);
        var leaseProvider = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ManagedSecurity.Common.Persistence.IJobLeaseProvider>(scope.ServiceProvider);
        await leaseProvider.RecordCheckpointAsync(payload.JobId, agentId, payload.StatePayload, payload.RequestedExtensionSeconds);
    }

    private async Task CompleteJobAsync(string agentId, JobCompletionPayload payload, CancellationToken ct)
    {
        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(_serviceProvider);
        var leaseProvider = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ManagedSecurity.Common.Persistence.IJobLeaseProvider>(scope.ServiceProvider);
        await leaseProvider.ReleaseLeaseAsync(payload.JobId, agentId);
    }

    private async Task FailJobAsync(string agentId, JobFailurePayload payload, CancellationToken ct)
    {
        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(_serviceProvider);
        var leaseProvider = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ManagedSecurity.Common.Persistence.IJobLeaseProvider>(scope.ServiceProvider);
        await leaseProvider.FailJobAsync(payload.JobId, agentId, payload.ErrorMessage);
    }
}
