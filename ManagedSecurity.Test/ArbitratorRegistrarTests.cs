using System.Net.WebSockets;
using ManagedSecurity.Orchestration.Arbitrator;
using ManagedSecurity.Test.Mocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedSecurity.Test;

[TestClass]
public class ArbitratorRegistrarTests
{
    [TestMethod]
    public void Arbitrator_Registrar_Adds_And_Extracts_Tunnels_Correctly()
    {
        // 1. Arrange
        IArbitratorRegistrar registrar = new ArbitratorRegistrar();
        var tunnel1 = new MockWebSocket();
        
        // 2. Act
        registrar.RegisterTunnel("scout-beta", tunnel1);
        
        // 3. Assert
        Assert.AreEqual(1, registrar.ActiveTunnelCount);
        bool found = registrar.TryGetTunnel("scout-beta", out var retrieved);
        
        Assert.IsTrue(found);
        Assert.AreSame(tunnel1, retrieved);
    }
    
    [TestMethod]
    public void Arbitrator_Registrar_Gracefully_Supersedes_Stale_Tunnels()
    {
        // 1. Arrange
        IArbitratorRegistrar registrar = new ArbitratorRegistrar();
        var originalTunnel = new MockWebSocket();
        var newTunnel = new MockWebSocket();
        
        // 2. Act - Initial registration
        registrar.RegisterTunnel("commander-alpha", originalTunnel);
        Assert.AreEqual(WebSocketState.Open, originalTunnel.State);
        
        // Rapid reconnect logic triggers organically
        registrar.RegisterTunnel("commander-alpha", newTunnel);
        
        // 3. Assert - The Arbitrator natively terminated the stale socket!
        Assert.AreEqual(WebSocketState.Closed, originalTunnel.State);
        Assert.AreEqual(WebSocketCloseStatus.ProtocolError, originalTunnel.CloseStatus);
        
        bool found = registrar.TryGetTunnel("commander-alpha", out var retrieved);
        Assert.IsTrue(found);
        Assert.AreSame(newTunnel, retrieved);
        Assert.AreEqual(1, registrar.ActiveTunnelCount);
    }
}
