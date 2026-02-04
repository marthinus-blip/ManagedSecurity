using System;
using ManagedSecurity.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedSecurity.Test
{
    [TestClass]
    public class HeaderTests
    {
        // Removed fixed MacSize constant based on S bit.

        public static byte[] CreateBaseHeader(byte ivLengthCode = 0, ushort lRaw = 0, ushort kiRaw = 0)
        {
            // Magic = 7 (3 bits) -> 0x7 -> << 29
            // Ver = 0 (2 bits)   -> << 27
            // Switch (S) = ivLengthCode (2 bits) -> << 25
            // Reserved = 0 (1 bit) -> << 24
            // L = lRaw (12 bits) -> << 12
            // KI = kiRaw (12 bits) -> << 0

            uint h = 0;
            h |= (uint)(0x07) << 29;         // Magic 111
            h |= (uint)(0x00 & 0x03) << 27;  // Version 0
            h |= (uint)(ivLengthCode & 0x03) << 25; // Switch
            h |= (uint)(0x00 & 0x01) << 24;  // Reserved
            h |= (uint)(lRaw & 0xFFF) << 12; // L (12 bits)
            h |= (uint)(kiRaw & 0xFFF);      // KI (12 bits)

            byte[] b = new byte[4];
            b[0] = (byte)((h >> 24) & 0xFF);
            b[1] = (byte)((h >> 16) & 0xFF);
            b[2] = (byte)((h >> 8) & 0xFF);
            b[3] = (byte)(h & 0xFF);

            return b;
        }

        public static byte[] ConstructFullMessage(byte[] header, int extensions, int ivLen, int macLen, int payloadLen)
        {
            int totalLen = header.Length + extensions + ivLen + macLen + payloadLen;
            byte[] msg = new byte[totalLen];
            Array.Copy(header, 0, msg, 0, 4);
            return msg;
        }

        [TestMethod]
        public void Parse_BaseCase_Success()
        {
            // Magic=7, S=0 (96bit IV, 128bit MAC), L=2000 (Fits in 12 bits!), KI=5
            ushort lLarge = 2000;
            byte[] raw = CreateBaseHeader(0, lLarge, 5);
            
            // Header(4) + IV(12) + MAC(16) + Payload(2000)
            // No extensions needed for L=2000 now!
            int total = 4 + 12 + 16 + 2000;
            byte[] msg = new byte[total];
            Array.Copy(raw, msg, 4);

            var h = new Bindings.Header(msg);

            Assert.AreEqual(12, h.IvLength);
            Assert.AreEqual(16, h.MacLength);
            Assert.AreEqual(2000, h.PayloadLength);
            Assert.AreEqual(5, h.KeyIndex);
            
            Assert.AreEqual(4, h.IvOffset); // No extensions
            Assert.AreEqual(4 + 12 + 16, h.PayloadOffset);
        }

        [TestMethod]
        public void Parse_InvalidMagic_Throws()
        {
            // Magic != 7
            byte[] raw = new byte[4]; // All zeros -> Magic=0
            Assert.ThrowsException<InvalidOperationException>(() => new Bindings.Header(raw));
        }

        [TestMethod]
        public void Parse_MacLength_Switch()
        {
            // S=1 (128bit IV, 256bit MAC), L=0, KI=0
            byte[] raw = CreateBaseHeader(1, 0, 0); 
            int total = 4 + 16 + 32 + 0;
            byte[] msg = new byte[total];
            Array.Copy(raw, msg, 4);

            var h = new Bindings.Header(msg);

            Assert.AreEqual(16, h.IvLength);
            Assert.AreEqual(32, h.MacLength);
        }

        [TestMethod]
        public void Parse_VariableLengthL_1ByteExtension()
        {
            // We want L to be > 2047? 
            // Max 12-bit value before extension marker?
            // With "leading one" logic on 12 bits:
            // 0... (0xxx xxxx xxxx) -> 0 to 2047.
            // 1... (10xx xxxx xxxx) -> Requires extension.
            // So to test extension, we need L to trigger the MSB.
            // Let's use 0x805 (1000 0000 0101).
            // Leading ones = 1. Stripped prefix (2 bits: 10) -> 00 0000 0101 (5).
            // Extension 1 byte: 0xFF.
            // Value = (5 << 8) | 0xFF = 1280 + 255 = 1535.
            
            ushort lRaw = 0x805; 
            byte[] baseH = CreateBaseHeader(0, lRaw, 0); 
            
            int extLen = 1;
            int payloadLen = 1535;
            int total = 4 + extLen + 12 + 16 + payloadLen;
            byte[] msg = new byte[total];
            
            Array.Copy(baseH, msg, 4);
            msg[4] = 0xFF; // Extension byte

            var h = new Bindings.Header(msg);

            Assert.AreEqual(1535, h.PayloadLength);
            Assert.AreEqual(5, h.IvOffset); 
        }
        
        [TestMethod]
        public void Parse_VariableLengthKI_2ByteExtension()
        {
            // KI 12 bits logic same as L now.
            // 2 extension bytes -> 110...
            // 0xC01 (1100 0000 0001).
            // Leading ones = 2. Strip 3 bits (110) -> 0 0000 0001 (1).
            ushort kiRaw = 0xC01;
            byte[] baseH = CreateBaseHeader(0, 0, kiRaw);

            int extLen = 2;
            int total = 4 + extLen + 12 + 16 + 0;
            byte[] msg = new byte[total];

            Array.Copy(baseH, msg, 4);
            msg[4] = 0x01;
            msg[5] = 0x02;

            var h = new Bindings.Header(msg);

            Assert.AreEqual(65794, h.KeyIndex);
            Assert.AreEqual(6, h.IvOffset); 
        }

        [TestMethod]
        public void Write_RoundTrip_Success()
        {
            int payloadLen = 2000;
            int keyIndex = 5;
            int profile = 0; // S=0

            int required = Bindings.Header.GetRequiredSize(payloadLen, keyIndex, profile);
            byte[] buffer = new byte[required];

            Bindings.Header.Write(buffer, payloadLen, keyIndex, profile);

            // Read back
            var h = new Bindings.Header(buffer);
            
            Assert.AreEqual(payloadLen, h.PayloadLength);
            Assert.AreEqual(keyIndex, h.KeyIndex);
            Assert.AreEqual(12, h.IvLength); // S=0
            Assert.AreEqual(16, h.MacLength);
        }

        [TestMethod]
        public void Write_ExtendedLength_1Byte_Success()
        {
            // Value that needs 1 extension byte (>= 2048)
            int payloadLen = 5000; 
            int keyIndex = 0;
            int profile = 1; // S=1

            int required = Bindings.Header.GetRequiredSize(payloadLen, keyIndex, profile);
            byte[] buffer = new byte[required];

            Bindings.Header.Write(buffer, payloadLen, keyIndex, profile);

            // Read back
            var h = new Bindings.Header(buffer);
            Assert.AreEqual(payloadLen, h.PayloadLength);
            Assert.AreEqual(16, h.IvLength); // S=1
            Assert.AreEqual(32, h.MacLength); // S=1

            // Verify size manually: 
            // Header(4) + L_Ext(1) + KI_Ext(0) + IV(16) + MAC(32) + Payload(5000)
            int expected = 4 + 1 + 0 + 16 + 32 + 5000;
            Assert.AreEqual(expected, required);
    }
}
}
