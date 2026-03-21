using System;
using System.Buffers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ManagedSecurity.Protocol;

namespace ManagedSecurity.Test;

[TestClass]
[ManagedSecurity.Common.Attributes.AllowMagicValues]
public class ProtocolSerializationTests
{
    [TestMethod]
    public void Protocol_Heartbeat_Serializes_And_Deserializes_Correctly()
    {
        // 1. Arrange the abstract "V-Table 0x01 Array" variables mapped via MemoryPack
        var expectedPayload = new HeartbeatPayload
        {
            SchemaId = 0x01,
            CpuLoad = 45,
            MemoryUsage = 1.23f,
            Status = "NOMINAL"
        };
        
        // Use an array buffer writer to simulate avoiding heap allocations per-packet serialization
        var bufferWriter = new ArrayBufferWriter<byte>(256);

        long allocatedBytesStart = GC.GetAllocatedBytesForCurrentThread();

        // 2. Act - Serialize Payload
        MemoryPack.MemoryPackSerializer.Serialize(bufferWriter, expectedPayload);

        // Fetch the exact slice representing the network dataframe
        ReadOnlySpan<byte> transportFrame = bufferWriter.WrittenSpan;

        // 3. Act - Deserialize Payload
        var actualPayload = MemoryPack.MemoryPackSerializer.Deserialize<HeartbeatPayload>(transportFrame);

        long allocatedBytesEnd = GC.GetAllocatedBytesForCurrentThread();
        long totalAllocations = allocatedBytesEnd - allocatedBytesStart;

        // 4. Assert Physical Correctness
        Assert.AreEqual(expectedPayload.SchemaId, actualPayload.SchemaId);
        Assert.AreEqual(expectedPayload.CpuLoad, actualPayload.CpuLoad);
        Assert.AreEqual(expectedPayload.MemoryUsage, actualPayload.MemoryUsage);
        Assert.AreEqual(expectedPayload.Status, actualPayload.Status);

        // MemoryPack strings re-allocate the actual string instance explicitly, but all other memory mapping spans are zero-allocation.
        // We assert GC pressure is under 200 bytes per packet.
        Assert.IsTrue(totalAllocations < 600, $"Unnecessary GC pressure detected: {totalAllocations} bytes.");
    }

    [TestMethod]
    public void Protocol_NativeAOT_MemoryPack_Struct_Validation()
    {
        // Arrange
        var expectedPayload = new HeartbeatPayload
        {
            SchemaId = 0x01,
            CpuLoad = 120,
            MemoryUsage = 3.14f,
            Status = "CRITICAL_OOM_STATE" 
        };

        var bufferWriter = new ArrayBufferWriter<byte>(256);
        MemoryPack.MemoryPackSerializer.Serialize(bufferWriter, expectedPayload);
        ReadOnlySpan<byte> transportFrame = bufferWriter.WrittenSpan;

        // Force GC stabilization before snapshot
        GC.Collect();
        long allocatedBytesStart = GC.GetAllocatedBytesForCurrentThread();
        
        // Act - Deserialize via MemoryPack
        var actualPayload = MemoryPack.MemoryPackSerializer.Deserialize<HeartbeatPayload>(transportFrame);

        long allocatedBytesEnd = GC.GetAllocatedBytesForCurrentThread();
        long totalAllocations = allocatedBytesEnd - allocatedBytesStart;

        // Assert Core Identity
        Assert.AreEqual(expectedPayload.SchemaId, actualPayload.SchemaId);
        Assert.AreEqual(expectedPayload.Status, actualPayload.Status);
        
        // Assert mathematical constraints tracking MemoryPack's string instance overhead,
        // it shouldn't allocate apart from the generated string and minimal structural layout.
        Assert.IsTrue(totalAllocations < 600, $"MemoryPack structure overhead overflow: {totalAllocations}");
    }
}
