using System;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedSecurity.Orchestration.Arbitrator;

/// <summary>
/// Defines the zero-allocation routing mechanics for binary C2 payloads natively traversing wss:// bounds [LS-OPT].
/// </summary>
public interface IArbitratorProtocolRouter
{
    /// <summary>
    /// Processes an incoming raw binary memory slice, parsing it securely and dispatching it to strictly defined system or extension handlers [CXFS-OPT].
    /// </summary>
    ValueTask RoutePayloadAsync(string agentId, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Constructs a completely deterministic ArbitratorFrame and natively injects it into the respective agent's WebSocket tunnel [NSLD-OPT].
    /// </summary>
    ValueTask SendAsync(string agentId, ushort opCode, uint correlationId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Structurally dispatches a payload explicitly and asynchronously awaits the mathematical deterministic response byte array strictly matched by CorrelationId natively [CXFS-OPT].
    /// </summary>
    Task<byte[]> InvokeAsync(string agentId, ushort opCode, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}
