using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ManagedSecurity.Core;
using ManagedSecurity.Common;

namespace ManagedSecurity.Test;

[TestClass]
public class SeekTableTests
{
    [TestMethod]
    public void SeekTable_E2E_RoundTrip_Success()
    {
        // 1. Setup
        var key = new byte[32];
        for (int i = 0; i < 32; i++) key[i] = (byte)i;
        var cipher = new Cipher(new SimpleKeyProvider(key));
        
        using var ms = new MemoryStream();
        string metadata = "CameraID=TestCam;Type=UnitTests";
        
        // 2. Write with Seek Points
        using (var crypto = new ManagedSecurityStream(ms, cipher, ManagedSecurityStreamMode.Encrypt, metadata: Encoding.UTF8.GetBytes(metadata)))
        {
            // Frame 1 (TS: 100)
            crypto.Write(Encoding.UTF8.GetBytes("Frame 1 Data"));
            crypto.FlushToFrame(100);
            
            // Frame 2 (TS: 200)
            crypto.Write(Encoding.UTF8.GetBytes("Frame 2 Data - Longer message to different offsets"));
            crypto.FlushToFrame(200);
            
            // Frame 3 (TS: 300)
            crypto.Write(Encoding.UTF8.GetBytes("Frame 3 Final"));
            crypto.FlushToFrame(300);
        }

        // 3. Verify Master Header via Stream
        byte[] finalData = ms.ToArray();
        var masterHeader = new MasterHeader(finalData);
        
        Assert.AreNotEqual(0u, masterHeader.SeekTableOffset, "SeekTableOffset should not be 0");
        Assert.IsTrue(masterHeader.SeekTableOffset < (ulong)finalData.Length, "SeekTableOffset should be within file bounds");
        Assert.AreEqual(metadata, Encoding.UTF8.GetString(masterHeader.GetMetadata(finalData)));

        // 4. Verify Seek Table Content
        var seekData = finalData.AsSpan((int)masterHeader.SeekTableOffset);
        var points = SeekTableSerializer.Deserialize(seekData);
        
        Assert.AreEqual(3, points.Count, "Should have 3 seek points");
        Assert.AreEqual(100u, points[0].RelativeTimestampMs);
        Assert.AreEqual(200u, points[1].RelativeTimestampMs);
        Assert.AreEqual(300u, points[2].RelativeTimestampMs);
        
        // Ensure offsets are increasing
        Assert.IsTrue(points[1].FileOffset > points[0].FileOffset, "Offset 2 > Offset 1");
        Assert.IsTrue(points[2].FileOffset > points[1].FileOffset, "Offset 3 > Offset 2");

        // 5. Verify Random Access Playback (Jump to Frame 2)
        using var readerMs = new MemoryStream(finalData);
        readerMs.Position = (long)points[1].FileOffset;
        
        using var cryptoIn = new ManagedSecurityStream(readerMs, cipher, ManagedSecurityStreamMode.Decrypt);
        
        // Note: When we jump into the middle of a stream, we MUST align with a frame.
        // Our protocol allows decrypting any frame as long as we know the sequence number (or use the header sequence).
        // For the sake of this test, we allow ManagedSecurityStream to handle the frame.
        
        byte[] buffer = new byte[100];
        int read = cryptoIn.Read(buffer);
        string result = Encoding.UTF8.GetString(buffer, 0, read);
        
        Assert.IsTrue(result.Contains("Frame 2 Data"), "Decrypted data at seek point should match Frame 2");
    }

    private class SimpleKeyProvider : IKeyProvider
    {
        private readonly ReadOnlyMemory<byte> _key;
        public SimpleKeyProvider(byte[] key) => _key = key;
        public ReadOnlyMemory<byte> GetKey(int keyIndex) => _key;
    }
}
