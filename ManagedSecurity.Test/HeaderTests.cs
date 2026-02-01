using System;
using ManagedSecurity.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedSecurity.Test
{
    [TestClass]
    public class HeaderTests
    {
        // Removed fixed MacSize constant as it is now variable based on S bit.

        public static byte[] CreateBaseHeader(byte ivLengthCode = 0, byte lRaw = 0, ushort kiRaw = 0)
        {
            // ECode = 127 (7 bits) -> 0x7F
            // S = ivLengthCode (2 bits) -> << 32-7-2 = 23
            // V = 0 (3 bits) 
            // L = lRaw (8 bits)
            // KI = kiRaw (12 bits)

            uint h = 0;
            h |= (uint)(0x7F) << 25;
            h |= (uint)(ivLengthCode & 0x03) << 23;
            h |= (uint)(lRaw & 0xFF) << 12;
            h |= (uint)(kiRaw & 0xFFF);

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
            // ECode=127, S=0 (96bit IV, 128bit MAC), L=10, KI=5
            byte[] raw = CreateBaseHeader(0, 10, 5);
            // Header(4) + IV(12) + MAC(16) + Payload(10)
            int total = 4 + 12 + 16 + 10;
            byte[] msg = new byte[total];
            Array.Copy(raw, msg, 4);

            var h = new Bindings.Header(msg);

            Assert.AreEqual(12, h.IvLength, "IV Length check");
            Assert.AreEqual(16, h.MacLength, "MAC Length check");
            Assert.AreEqual(10, h.PayloadLength, "Payload Length check");
            Assert.AreEqual(5, h.KeyIndex, "Key Index check");
            
            Assert.AreEqual(4, h.IvOffset); // No extensions
            Assert.AreEqual(4 + 12, h.MacOffset);
            Assert.AreEqual(4 + 12 + 16, h.PayloadOffset);
        }

        [TestMethod]
        public void Parse_InvalidECode_Throws()
        {
            // ECode != 127
            byte[] raw = new byte[4]; // All zeros
            Assert.ThrowsException<InvalidOperationException>(() => new Bindings.Header(raw));
        }

        [TestMethod]
        public void Parse_MacLength_Switch()
        {
            // S=1 (128bit IV, 256bit MAC), L=0, KI=0
            byte[] raw = CreateBaseHeader(1, 0, 0); 
            // Header(4) + IV(16) + MAC(32) + Payload(0)
            int total = 4 + 16 + 32 + 0;
            byte[] msg = new byte[total];
            Array.Copy(raw, msg, 4);

            var h = new Bindings.Header(msg);

            Assert.AreEqual(16, h.IvLength);
            Assert.AreEqual(32, h.MacLength);
            Assert.AreEqual(4, h.IvOffset);
            Assert.AreEqual(4 + 16, h.MacOffset);
            Assert.AreEqual(4 + 16 + 32, h.PayloadOffset);
        }

        [TestMethod]
        public void Parse_VariableLengthL_1ByteExtension()
        {
            // L > 127 (1 extension byte) -> S=0 default
            // S=0 -> IV=12, MAC=16
            
            // Construct Header
            byte lRaw = 0x81; 
            byte[] baseH = CreateBaseHeader(0, lRaw, 0); 
            
            int extLen = 1;
            int payloadLen = 511;
            // Header(4) + Ext(1) + IV(12) + MAC(16) + Payload(511)
            int total = 4 + extLen + 12 + 16 + payloadLen;
            byte[] msg = new byte[total];
            
            Array.Copy(baseH, msg, 4);
            msg[4] = 0xFF; // Extension byte

            var h = new Bindings.Header(msg);

            Assert.AreEqual(511, h.PayloadLength);
            Assert.AreEqual(5, h.IvOffset); // 4 + 1 extension
            Assert.AreEqual(5 + 12 + 16, h.PayloadOffset);
        }
        
        [TestMethod]
        public void Parse_VariableLengthKI_2ByteExtension()
        {
            // KI 2 extension bytes -> S=0 default
            ushort kiRaw = 0xC01;
            byte[] baseH = CreateBaseHeader(0, 0, kiRaw);

            int extLen = 2;
            // Header(4) + Ext(2) + IV(12) + MAC(16) + Payload(0)
            int total = 4 + extLen + 12 + 16 + 0;
            byte[] msg = new byte[total];

            Array.Copy(baseH, msg, 4);
            msg[4] = 0x01;
            msg[5] = 0x02;

            var h = new Bindings.Header(msg);

            Assert.AreEqual(65794, h.KeyIndex);
            Assert.AreEqual(6, h.IvOffset); 
        }
    }
}
