using System;
using System.Buffers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ManagedSecurity.Protocol;

namespace ManagedSecurity.Test;

[TestClass]
public class ProtocolSerializationTests
{
    [TestMethod]
    public void Protocol_Heartbeat_Serializes_And_Deserializes_Correctly()
    {
        // 1. Arrange the abstract "V-Table 0x01 Array" variables
        uint expectedSchemaId = 0x01; // E.g. Heartbeat V1
        int expectedCpuLoad = 45;
        float expectedMemoryUsage = 1.23f;
        string expectedStatus = "NOMINAL";

        // Stackalloc completely bypasses the GC, providing pure NativeAOT memory segments
        Span<byte> buffer = stackalloc byte[256];

        long allocatedBytesStart = GC.GetAllocatedBytesForCurrentThread();

        // 2. Act - Serialize Payload
        var writer = new SentinelPayloadWriter(buffer, expectedSchemaId);
        writer.Write(expectedCpuLoad);
        writer.Write(expectedMemoryUsage);
        writer.WriteString(expectedStatus);

        // Fetch the exact slice representing the network dataframe
        ReadOnlySpan<byte> transportFrame = writer.WrittenSpan;

        // 3. Act - Deserialize Payload
        var reader = new SentinelPayloadReader(transportFrame);
        uint actualSchemaId = reader.SchemaId;
        int actualCpuLoad = reader.ReadInt32();
        float actualMemoryUsage = reader.ReadSingle();
        string actualStatus = reader.ReadString();

        long allocatedBytesEnd = GC.GetAllocatedBytesForCurrentThread();
        long totalAllocations = allocatedBytesEnd - allocatedBytesStart;

        // 4. Assert Physical Correctness
        Assert.AreEqual(expectedSchemaId, actualSchemaId);
        Assert.AreEqual(expectedCpuLoad, actualCpuLoad);
        Assert.AreEqual(expectedMemoryUsage, actualMemoryUsage);
        Assert.AreEqual(expectedStatus, actualStatus);

        // Note: ReadString() explicitly allocates a managed string object representation, triggering ~38 bytes GC pressure.
        // If we expect less than 100 bytes of allocation, it proves the framework serializers are entirely transparent.
        Assert.IsTrue(totalAllocations < 100, $"Unnecessary GC pressure detected: {totalAllocations} bytes.");
    }

    [TestMethod]
    public void Protocol_NativeAOT_ZeroAllocation_StringSpan_Validation()
    {
        // Arrange
        uint expectedSchemaId = 0x01;
        string expectedStatus = "OFFLINE_SYSERR_NATIVE_BOUNDS"; // 28 characters

        Span<byte> buffer = stackalloc byte[256];
        var writer = new SentinelPayloadWriter(buffer, expectedSchemaId);
        writer.WriteString(expectedStatus);
        ReadOnlySpan<byte> transportFrame = writer.WrittenSpan;

        Span<char> charBuffer = stackalloc char[256]; // Stack-allocated char array for the UI bridge

        // Force GC stabilization before snapshot
        GC.Collect();
        long allocatedBytesStart = GC.GetAllocatedBytesForCurrentThread();
        
        // Act - Deserialize completely natively
        var reader = new SentinelPayloadReader(transportFrame);
        uint actualSchemaId = reader.SchemaId;
        int charsWritten = reader.ReadStringSpan(charBuffer);

        long allocatedBytesEnd = GC.GetAllocatedBytesForCurrentThread();
        long totalAllocations = allocatedBytesEnd - allocatedBytesStart;

        // Assert Core Identity
        Assert.AreEqual(expectedSchemaId, actualSchemaId);
        Assert.AreEqual(expectedStatus.Length, charsWritten);
        
        var decodedSlice = charBuffer.Slice(0, charsWritten);
        Assert.IsTrue(expectedStatus.AsSpan().SequenceEqual(decodedSlice));
        
        // Assert Absolute Mathematical Proof (0 Bytes Allocated)
        Assert.AreEqual(0, totalAllocations);
    }
}
