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

        private readonly ReadOnlyMemory<byte> _data;

        public Header(byte[] data) : this(new ReadOnlyMemory<byte>(data)) { }

        public Header(ReadOnlyMemory<byte> data)
        {
            if (data.Length < FixedHeaderSize)
                throw new ArgumentException("Data too short for header", nameof(data));

            _data = data;
            var span = data.Span;

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
            TotalLength = PayloadOffset + PayloadLength;

            if (data.Length < TotalLength)
               throw new ArgumentException($"Data length {data.Length} is less than required {TotalLength}");
        }

        // Variable Length Decoder for 12-bit Base Fields
        private static int DecodeVariableLength12(ushort baseValue, ReadOnlyMemory<byte> data, ref int offset)
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
                // Safety clamp
                if (extensions > 8) break; 
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
            var span = data.Span;
            for (int i = 0; i < extensions; i++)
            {
                if (offset >= data.Length) throw new IndexOutOfRangeException("Not enough data for extensions");
                value = (value << 8) | span[offset++];
            }
            return value;
        }

        public ReadOnlySpan<byte> Iv => _data.Span.Slice(IvOffset, IvLength);
        public ReadOnlySpan<byte> Mac => _data.Span.Slice(MacOffset, MacLength);
        public ReadOnlySpan<byte> Payload => _data.Span.Slice(PayloadOffset, PayloadLength);
    }
}