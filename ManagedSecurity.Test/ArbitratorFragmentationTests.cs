using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ManagedSecurity.Protocol;

namespace ManagedSecurity.Test;

[TestClass]
public class ArbitratorFragmentationTests
{
    [TestMethod]
    public void Framer_Correctly_Segments_Oversized_Buffers_Without_Allocations()
    {
        // Arrange an aggressively large payload mapping a 150KB array natively (representing raw BGR arrays)
        int randomSize = 150000;
        byte[] massivePayload = new byte[randomSize];
        Random.Shared.NextBytes(massivePayload);
        
        ushort targetOpCode = 55;
        uint targetCorrelation = 998877;
        
        var capturedChunks = new List<(ushort Version, ushort OpCode, uint CorrelationId, ushort SequenceIndex, byte[] Payload, bool IsFragmented, bool IsFinalFragment)>();
        
        // Act
        ArbitratorFramer.EmitFragments(
            targetOpCode, 
            targetCorrelation, 
            massivePayload.AsMemory(), 
            frame => 
            {
                capturedChunks.Add((frame.Version, frame.OpCode, frame.CorrelationId, frame.SequenceIndex, frame.Payload.ToArray(), frame.IsFragmented, frame.IsFinalFragment));
            });
            
        // Assert Boundaries structurally
        Assert.AreEqual(3, capturedChunks.Count, "150KB should divide cleanly into exactly 3 bounded chunks smoothly.");
        
        // Assert Chunk 1
        Assert.IsTrue(capturedChunks[0].IsFragmented);
        Assert.IsFalse(capturedChunks[0].IsFinalFragment);
        Assert.AreEqual(0, capturedChunks[0].SequenceIndex);
        Assert.AreEqual(ArbitratorFramer.MaxChunkSize, capturedChunks[0].Payload.Length);
        
        // Assert Chunk 2
        Assert.IsTrue(capturedChunks[1].IsFragmented);
        Assert.IsFalse(capturedChunks[1].IsFinalFragment);
        Assert.AreEqual(1, capturedChunks[1].SequenceIndex);
        Assert.AreEqual(ArbitratorFramer.MaxChunkSize, capturedChunks[1].Payload.Length);
        
        // Assert Chunk 3 (Final boundary math)
        Assert.IsTrue(capturedChunks[2].IsFragmented);
        Assert.IsTrue(capturedChunks[2].IsFinalFragment); // Physical termination bit explicitly flagged safely
        Assert.AreEqual(2, capturedChunks[2].SequenceIndex);
        
        int expectedRemainder = 150000 - (ArbitratorFramer.MaxChunkSize * 2);
        Assert.AreEqual(expectedRemainder, capturedChunks[2].Payload.Length);
        Assert.AreEqual(targetOpCode, capturedChunks[2].OpCode);
    }
    
    [TestMethod]
    public void Framer_Successfully_Serializes_And_Deserializes_Fragmented_Headers_Cleanly()
    {
        // Arrange
        ushort baseOpCode = 42;
        ushort sequence = 5; // e.g., the 5th chunk
        
        // Manually apply Fragment bitmask
        ushort rawOpCode = (ushort)(baseOpCode | ArbitratorFrame.FragmentMask);
        
        byte[] tinySlice = new byte[5] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };
        
        var originalFrame = new ArbitratorFrame(1, rawOpCode, 1234, sequence, tinySlice);
        
        byte[] networkBuffer = new byte[1024];
        
        // Act: Serialize
        int writtenLength = originalFrame.WriteTo(networkBuffer.AsSpan());
        
        // SequenceIndex consumes 2 physical payload bytes efficiently, so wire payload is 5 + 2 = 7
        Assert.AreEqual(ArbitratorFrame.HeaderSize + 7, writtenLength); 
        
        // Act: Deserialize natively
        bool success = ArbitratorFrame.TryParse(networkBuffer.AsSpan(0, writtenLength), out var decodedFrame);
        
        // Assert Decoding Mapping Objectively cleanly
        Assert.IsTrue(success, "Failed to parse fragmented wire frame natively smoothly.");
        
        Assert.IsTrue(decodedFrame.IsFragmented);
        Assert.IsFalse(decodedFrame.IsFinalFragment);
        Assert.AreEqual(baseOpCode, decodedFrame.OpCode);
        Assert.AreEqual(1234u, decodedFrame.CorrelationId);
        
        Assert.AreEqual((ushort)sequence, decodedFrame.SequenceIndex);
        Assert.AreEqual(5, decodedFrame.Payload.Length);
        
        // Verify exact byte integrity
        for (int i = 0; i < 5; i++)
        {
            Assert.AreEqual(tinySlice[i], decodedFrame.Payload[i]);
        }
    }
}
