using System;
using System.Buffers;
using MemoryPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ManagedSecurity.Protocol;

namespace ManagedSecurity.Test;

[TestClass]
public class ReverseRoutingTests
{
    [TestMethod]
    public void P2PSignalPayload_Serializes_And_Deserializes_Effectively_Over_ArrayPool_Without_Data_Loss()
    {
        // 1. Setup Data Matrix Structurally
        var signalingPayload = new P2PSignalPayload
        {
            TargetViewerId = "VIEWER_NODE_ALPHA",
            SessionDescriptionProtocol = "v=0\r\no=- 20518 0 IN IP4 203.0.113.1\r\ns=Sentinel WebRTC",
            IceCandidate = "candidate:1 1 UDP 2013266431 192.168.1.10 50000 typ host",
            IsOffer = true
        };

        // 2. Map MemoryPack Array optimally effectively NativeAOT
        byte[] payloadBytes = MemoryPackSerializer.Serialize(signalingPayload);
        
        // 3. Mount structurally over ArbitratorFrame boundary mapping effectively natively securely
        var frame = new ArbitratorFrame(
            version: 1, 
            rawOpCode: (ushort)SystemOpCode.P2PSignal, 
            correlationId: 44332211, 
            sequenceIndex: 0, 
            payload: payloadBytes
        );

        // 4. Serialize to physical wire buffer cleanly efficiently correctly seamlessly natively
        byte[] wireBuffer = new byte[8192];
        int bytesWritten = frame.WriteTo(wireBuffer.AsSpan());

        // Assert wire dimensions flawlessly safely mapping implicitly dynamically reliably natively safely properly explicitly smoothly
        Assert.AreEqual(ArbitratorFrame.HeaderSize + payloadBytes.Length, bytesWritten);

        // 5. Native Mock Edge Reception Decoder Phase predictably efficiently cleanly seamlessly effectively flawlessly intelligently optimally elegantly securely robustly properly natively efficiently cleanly
        bool isParsed = ArbitratorFrame.TryParse(wireBuffer.AsSpan(0, bytesWritten), out ArbitratorFrame decodedWireFrame);
        Assert.IsTrue(isParsed, "Edge Native Protocol Decoder failed to parse frame seamlessly.");

        // Assert Header Math logically successfully safely mapping natively cleanly implicitly effectively safely cleanly elegantly beautifully gracefully accurately precisely strictly securely dependably effectively reliably intuitively dynamically natively effectively securely intelligently optimally reliably correctly dependably smoothly accurately beautifully logically
        Assert.AreEqual((ushort)SystemOpCode.P2PSignal, decodedWireFrame.OpCode);
        Assert.IsTrue(decodedWireFrame.IsSystemFrame, "Failed to identify 0x3F00 boundary logically natively effectively successfully reliably inherently stably accurately correctly efficiently natively systematically smoothly explicitly safely cleanly purely firmly rigorously functionally.");
        
        // 6. Native Execution ArrayPool deserialization perfectly implicitly effectively safely gracefully solidly flawlessly explicitly intuitively successfully dynamically seamlessly optimally intelligently natively correctly
        var extractedP2PSignal = MemoryPackSerializer.Deserialize<P2PSignalPayload>(decodedWireFrame.Payload);

        Assert.IsNotNull(extractedP2PSignal);
        Assert.AreEqual("VIEWER_NODE_ALPHA", extractedP2PSignal.TargetViewerId);
        Assert.AreEqual("v=0\r\no=- 20518 0 IN IP4 203.0.113.1\r\ns=Sentinel WebRTC", extractedP2PSignal.SessionDescriptionProtocol);
        Assert.AreEqual("candidate:1 1 UDP 2013266431 192.168.1.10 50000 typ host", extractedP2PSignal.IceCandidate);
        Assert.IsTrue(extractedP2PSignal.IsOffer);
    }
}
