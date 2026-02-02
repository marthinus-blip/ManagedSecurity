namespace ManagedSecurity.Common;

public partial class Bindings {

    public readonly struct Header
    {
        public const int FixedHeaderSize = 4; // 32 bits = 4 bytes

        // Raw 32-bit header parts
        private readonly uint _rawHeader;
        
        // Parsed properties
        public int IvLength { get; }
        public int MacLength { get; }
        public int PayloadLength { get; }
        public int KeyIndex { get; }
        
        // Calculated offsets
        public int IvOffset { get; }
        public int MacOffset { get; }
        public int PayloadOffset { get; }
        public int TotalLength { get; }

        public Header(ReadOnlySpan<byte> data)
        {
            if (data.Length < FixedHeaderSize)
                throw new ArgumentException("Data too short for header", nameof(data));

            var span = data;

            // Reconstruct 32-bit word (Big Endian)
            uint h = (uint)((span[0] << 24) | (span[1] << 16) | (span[2] << 8) | span[3]);
            _rawHeader = h;

            // 1. Magic (Bits 0-2 -> Top 3 bits)
            // Shift down 29 bits (32 - 3)
            int magic = (int)((h >> 29) & 0x07); 
            if (magic != 7) // 111b
                throw new InvalidOperationException($"Invalid Header Magic: {magic}. Expected 7.");

            // 2. Version (Bits 3-4 -> Next 2 bits)
            // Shift down 27 bits (29 - 2)
            int v = (int)((h >> 27) & 0x03);
            if (v != 0)
                throw new NotImplementedException($"Version {v} not implemented.");

            // 3. Switch (Bits 5-6 -> Next 2 bits)
            // Shift down 25 bits (27 - 2)
            int s = (int)((h >> 25) & 0x03);

            if (s == 0)
            {
                IvLength = 12;
                MacLength = 16;
            }
            else if (s == 1)
            {
                IvLength = 16;
                MacLength = 32;
            }
            else
            {
                throw new NotSupportedException($"Switch value S={s} is not supported.");
            }

            // 4. Reserved (Bit 7 -> Next 1 bit)
            // Shift down 24 bits. (Just ignore for now)

            // Track offset for variable fields
            int currentOffset = FixedHeaderSize;

            // 5. L - Payload Length (Bits 8-19 -> 12 bits)
            // Shift down 12 bits (24 - 12) -> wait, 32 - 3 - 2 - 2 - 1 = 24 remaining bits?
            // Layout: [3 Magic][2 Ver][2 Switch][1 Res] [12 L] [12 KI]
            // Total bits used before L: 3+2+2+1 = 8.
            // Remaining 24 bits. 
            // L is top 12 of remaining 24. 
            // Shift down 12 bits to get L.
            ushort lRaw = (ushort)((h >> 12) & 0xFFF);
            PayloadLength = DecodeVariableLength12(lRaw, data, ref currentOffset);

            // 6. KI - Key Index (Bits 20-31 -> Bottom 12 bits)
            ushort kiRaw = (ushort)(h & 0xFFF); 
            KeyIndex = DecodeVariableLength12(kiRaw, data, ref currentOffset);

            // Calculate Offsets
            // Layout: [FixedHeader+Ext] [IV] [MAC] [Payload]
            IvOffset = currentOffset;
            MacOffset = IvOffset + IvLength;
            PayloadOffset = MacOffset + MacLength;
            
            // Use long to prevent overflow during length check
            long total = (long)PayloadOffset + PayloadLength;
            if (total > int.MaxValue)
                throw new ArgumentException("Total message length exceeds supported limits.");
            
            TotalLength = (int)total;

            if (data.Length < TotalLength)
                throw new ArgumentException($"Data length {data.Length} is less than required {TotalLength}");
        }

        // Variable Length Decoder for 12-bit Base Fields
        private static int DecodeVariableLength12(ushort baseValue, ReadOnlySpan<byte> data, ref int offset)
        {
            // Count leading ones in the 12-bit value (0x800 is High bit for 12-bit int)
            // 0x800 = 1000 0000 0000
            int extensions = 0;
            ushort mask = 0x800;
            ushort temp = baseValue;
            
            while ((temp & mask) != 0)
            {
                extensions++;
                temp = (ushort)(temp << 1);
                // Safety clamp: Protocol allows more, but we cap at 3 for implementation safety (max ~1GB)
                if (extensions > 3) 
                    throw new ArgumentException("Too many field extension bytes.", nameof(data)); 
            }

            if (extensions == 0) return baseValue;

            // Remove prefix bits from the top of the 12-bit value.
            // If 1 extension: 10xxxxxxxxxx (12 bits) -> remove top 1 bit.
            // Keep bits = 12 - 1 - extensions
            // e.g. ext=1: remove leading '1'. keep 11 bits? 
            // Wait, standard varint: "1xxxxxxx" -> remove '1'.
            // Here: "1...0..." -> the zeros delineate? 
            // The Plan: "1...10..." N ones means N extensions.
            // So for ext=1: Pattern is "10...". We remove "1".
            // For ext=2: Pattern is "110...". We remove "11".
            // Actually, we usually keep the '0' as part of the value or discard?
            // Let's assume we discard the marker bits entirely.
            // Marker is N ones. So we mask off top N bits?
            // "10..." -> Remove '1'. 
            // "110..." -> Remove '11'.
            
            // Mask to keep bottom (12 - extensions) bits
            int keepBits = 12 - extensions; 
            // But wait, the stop bit '0' is usually implicit in the value or stripped? 
            // In typical UTF-8 style: 110xxxxx -> 5 bits payload.
            // Here: 110... -> top 2 are marker. 3rd is a 0? 
            // Let's stick to the previous implementation logic:
            // "Remove the prefix bits".
            // If loop stopped at '0' bit, that '0' is part of the value? 
            // Previous logic: value = baseValue & (0xFF >> (extensions + 1));
            // That implied stripping N ones AND the following zero? Or just N+1 bits?
            // Let's strip just the N ones for max capacity?
            // Actually, standard is usually: Marker = leading ones + 0.
            // So N=1 -> "10...". We strip 2 bits.
            // N=2 -> "110...". We strip 3 bits.
            
            int bitsToStrip = extensions + 1;
            int value = baseValue & ((1 << (12 - bitsToStrip)) - 1);

            // Read extension bytes
            for (int i = 0; i < extensions; i++)
            {
                if (offset >= data.Length) 
                    throw new ArgumentException("Data too short for header extensions.", nameof(data));
                value = (value << 8) | data[offset++];
            }
            return value;
        }

        public readonly ReadOnlySpan<byte> GetIv(ReadOnlySpan<byte> message) => message.Slice(IvOffset, IvLength);
        public readonly ReadOnlySpan<byte> GetMac(ReadOnlySpan<byte> message) => message.Slice(MacOffset, MacLength);
        public readonly ReadOnlySpan<byte> GetPayload(ReadOnlySpan<byte> message) => message.Slice(PayloadOffset, PayloadLength);

        public readonly Span<byte> GetIv(Span<byte> message) => message.Slice(IvOffset, IvLength);
        public readonly Span<byte> GetMac(Span<byte> message) => message.Slice(MacOffset, MacLength);
        public readonly Span<byte> GetPayload(Span<byte> message) => message.Slice(PayloadOffset, PayloadLength);

        // =========================================================================================
        // WRITE / BUILDER API
        // =========================================================================================

        /// <summary>
        /// Calculates the total size in bytes required for a message with the given parameters and empty extensions.
        /// </summary>
        public static int GetRequiredSize(int payloadLength, int keyIndex, bool highSecurityMode)
        {
            int baseSize = FixedHeaderSize;
            
            // Add extensions size for P
            baseSize += GetExtensionCount(payloadLength);
            // Add extensions size for KI
            baseSize += GetExtensionCount(keyIndex);

            int ivLen = highSecurityMode ? 16 : 12;
            int macLen = highSecurityMode ? 32 : 16;
            
            // Format: Header+Ext + IV + MAC + Payload
            return baseSize + ivLen + macLen + payloadLength;
        }

        public static void Write(Span<byte> destination, int payloadLength, int keyIndex, bool highSecurityMode)
        {
            if (destination.Length < GetRequiredSize(payloadLength, keyIndex, highSecurityMode))
                throw new ArgumentException("Destination buffer too small.");

            uint h = 0;
            // Magic (111)
            h |= (7u << 29);
            // Version (0)
            h |= (0u << 27);
            // Switch
            h |= ((highSecurityMode ? 1u : 0u) << 25);
            // Reserved (0)
            h |= (0u << 24);

            int currentOffset = FixedHeaderSize;

            // Encode L
            ushort lBase = EncodeVariableLength12(payloadLength, destination, ref currentOffset);
            h |= ((uint)lBase << 12);

            // Encode KI
            ushort kiBase = EncodeVariableLength12(keyIndex, destination, ref currentOffset);
            h |= (uint)kiBase; // Bottom 12 bits

            // Write 32-bit header (Big Endian)
            destination[0] = (byte)((h >> 24) & 0xFF);
            destination[1] = (byte)((h >> 16) & 0xFF);
            destination[2] = (byte)((h >> 8) & 0xFF);
            destination[3] = (byte)(h & 0xFF);
        }

        private static int GetExtensionCount(int value)
        {
            if (value < (1 << 11)) return 0; // 0... fits in 11 bits (top bit 0) -> < 2048
            if (value < (1 << (10 + 8))) return 1; // 10... fits in 10 base + 8 ext -> 18 bits -> < 262144
            if (value < (1 << (9 + 16))) return 2; // 110... fits in 9 base + 16 ext -> 25 bits -> ~33MB
            // Add more tiers if needed
            throw new ArgumentOutOfRangeException(nameof(value), "Value too large for current encoder implementation.");
        }

        private static ushort EncodeVariableLength12(int value, Span<byte> destination, ref int offset)
        {
            // Case 0: No extensions (0...)
            // Must fit in 11 bits? Wait, 12 bits available. 
            // If bit 11 (Top) is 0, we have 11 bits of capacity? No.
            // Protocol: "0..." means Top bit is 0. So value must be < 2^11 (2048).
            // Example: 2047 is 0111 1111 1111. Fits.
            // Example: 2048 is 1000 0000 0000. Top bit is 1. Triggers extension logic.
            if (value < (1 << 11))
            {
                return (ushort)value;
            }

            // Case 1: 1 Extension (10...)
            // Capacity: 18 bits.
            if (value < (1 << 18))
            {
                // We need to output 12 bits for base.
                // Top 2 bits: 10
                // Remaining 10 bits: Top 10 bits of value.
                // Extension byte: Bottom 8 bits of value.
                
                // destination[offset] = bottom 8
                destination[offset++] = (byte)(value & 0xFF);
                
                // Remainder = value >> 8.
                // We need to fit this into the bottom 10 bits of the base.
                int remainder = value >> 8;
                
                // Construct base: 10xxxxxx xxxxxxxx (12 bits)
                // 10 = 0x2 -> shift to top 2 of 12 (bits 10,11).
                // 1000 0000 0000 = 0x800.
                // Mask for top 2 bits: 1100 0000 0000 -> 0xC00.
                // Wait, prefix is "10". 
                // binary: 10... 
                // 0x800 (1000...)
                // We want bits 11=1, 10=0.
                // So 0x800.
                
                ushort baseVal = (ushort)(0x800 | (remainder & 0x3FF));
                return baseVal;
            }

            // Case 2: 2 Extensions (110...)
            // Capacity: 25 bits.
            if (value < (1 << 25))
            {
                // Output bottom 16 bits to extensions (Big Endian usually preferred? Or Little?
                // Readers use: value = (value << 8) | span[offset++]
                // So we must write Most Significant Extension first?
                // Let's re-read reader:
                // Reader: value starts with Base.
                // loop 0: value = (value << 8) | ext1
                // loop 1: value = (value << 8) | ext2
                // So we must write ext1 THEN ext2.
                destination[offset++] = (byte)((value >> 8) & 0xFF); // Ext1
                destination[offset++] = (byte)(value & 0xFF);       // Ext2

                // Construct base: 110xxxxxxxxx
                // 110... -> 0xC00 (1100...) ?
                // 1000... = 8. 1100... = C. 
                // We want bits 11=1, 10=1, 9=0.
                // 1100 0000 0000 = 0xC00.
                
                int remainder = value >> 16;
                ushort baseVal = (ushort)(0xC00 | (remainder & 0x1FF));
                return baseVal;
            }
            
            throw new ArgumentOutOfRangeException(nameof(value), "Value too large to encode.");
        }
    }
}