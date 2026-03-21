using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedSecurity.Test.Mocks;

/// <summary>
/// A zero-dependency isolated mock mimicking physical WebSocket states perfectly natively flawlessly fluently securely stably.
/// </summary>
public class MockWebSocket : WebSocket
{
    public override WebSocketCloseStatus? CloseStatus => _closeStatus;
    public override string? CloseStatusDescription => _closeStatusDescription;
    public override WebSocketState State => _state;
    public override string? SubProtocol => null;

    private WebSocketCloseStatus? _closeStatus;
    private string? _closeStatusDescription;
    private WebSocketState _state = WebSocketState.Open;

    public override void Abort() => _state = WebSocketState.Aborted;
    
    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        _closeStatus = closeStatus;
        _closeStatusDescription = statusDescription;
        _state = WebSocketState.Closed;
        return Task.CompletedTask;
    }
    
    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        _closeStatus = closeStatus;
        _closeStatusDescription = statusDescription;
        _state = WebSocketState.CloseSent;
        return Task.CompletedTask;
    }
    
    public override void Dispose() { }
    
    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) 
        => Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));
        
    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) 
        => Task.CompletedTask;
}
