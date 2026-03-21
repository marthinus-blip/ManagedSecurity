using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using ManagedSecurity.Protocol;
using Microsoft.Extensions.Logging;

namespace ManagedSecurity.Orchestration.Arbitrator;

/// <summary>
/// A centralized router translating C2 WebSockets securely utilizing the zero-allocation ArbitratorFrame structurally.
/// [CXFS-OPT]
/// </summary>
public class ArbitratorProtocolRouter : IArbitratorProtocolRouter
{
    private const string LogSystemMapped = "[SYSTEM_ROUTER] Agent {0} mapped system OpCode {1:X4} (Correlation: {2})";
    private const string LogExtensionMapped = "[EXTENSION_ROUTER] Agent {0} mapped developer OpCode {1:X4} (Correlation: {2})";
    private const string LogFaultFragmented = "[ROUTER_FAULT] Agent {0} submitted a fragmented or invalid payload envelope.";
    private const string ErrorTunnelInaccessible = "Edge Scout {0} tunnel is fundamentally inaccessible.";
    private const string ErrorCorrelationCollision = "Correlation ID collision dynamically detected.";

    private readonly IArbitratorRegistrar _registrar;
    private readonly Microsoft.Extensions.Logging.ILogger<ArbitratorProtocolRouter> _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, TaskCompletionSource<byte[]>> _pendingPromises;
    private int _nextCorrelationId;

    public ArbitratorProtocolRouter(IArbitratorRegistrar registrar, Microsoft.Extensions.Logging.ILogger<ArbitratorProtocolRouter> logger)
    {
        _registrar = registrar;
        _logger = logger;
        _pendingPromises = new System.Collections.Concurrent.ConcurrentDictionary<uint, TaskCompletionSource<byte[]>>();
    }

    /// <summary>
    /// Processes an incoming raw binary memory slice, parsing it securely and dispatching it [INSC-OPT].
    /// </summary>
    public ValueTask RoutePayloadAsync(string agentId, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (ArbitratorFrame.TryParse(buffer.Span, out var frame))
        {
            // If the CorrelationId perfectly aligns with our pending Promise matrix natively, resolve and drop [CXFS-OPT].
            if (_pendingPromises.TryRemove(frame.CorrelationId, out var completionSource))
            {
                // Array allocation explicitly required here to safely sever the read-only ArrayPool slice bounds [EE-OPT]
                completionSource.TrySetResult(frame.Payload.ToArray());
                return ValueTask.CompletedTask;
            }

            // Execute the system 0xFF00 binary bifurcation logically internally [EE-OPT]
            if (frame.IsSystemFrame)
            {
                // [NSLD-OPT]
                _logger.LogInformation(LogSystemMapped, agentId, frame.OpCode, frame.CorrelationId);
            }
            else
            {
                // [CXFS-OPT]
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
    /// Constructs a completely deterministic ArbitratorFrame and natively injects it into the WebSocket [NSLD-OPT].
    /// </summary>
    public async ValueTask SendAsync(string agentId, ushort opCode, uint correlationId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (!_registrar.TryGetTunnel(agentId, out var webSocket))
        {
            throw new InvalidOperationException(string.Format(ErrorTunnelInaccessible, agentId));
        }

        var frameLength = ArbitratorFrame.HeaderSize + payload.Length;
        int writtenBytes = 0;
        
        // Execute an explicit ArrayPool lease to bypass GC allocation penalties globally [LS-OPT]
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
        // Frame version is intrinsically v1 [EE-OPT]
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
        
        // Asynchronously block logically until the RoutePayloadAsync interceptor magically resolves this CorrelationId [NSLD-OPT]
        return await tcs.Task;
    }
}
