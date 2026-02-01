namespace ManagedSecurity.Common;

public partial class Bindings {

/*
Let's discuss. I want to make something that manages cypher text. It should be extenable. Perhaps a well known binary format can be used, where a 32 bit wide header is defined that describes the suffixed data (Payload)
|| Terms: MESSAGE = Header + IV + MAC + Payload
|| Layout-overview: (ECode: 7 bits) (S: 2 bits) (V: 3 bits) (L: 8 bits) (KI: 12 bits) (LX: 0..n * 8 bits) (KIX: 0..n * 8 bits) (IV: 96 or 128 bits) (MAC: 256 bits) (Payload: L + LX)
|| Layout (Bits 1 to 7 ): stores encoding tag (ECode) (value should always be 127). 127 is prime, and when the MESSAGE gets encoded into other format (e.g. Base64 string) then we should (probably) be able to detect the correct decdoing step 
|| Layout (bits 8 to 9):  A switch control that defiens the width of  IV; (value == 00b) -> IV is 96 bits wide; (value == 01b) IV is 128 bits wide 
|| Layout (bits 10 to 12): The version (V) of the implementaion. (value != 111) -> version 000b to 110b available; (value == 111) -> sufix a extended version to the end of the header (TODO: not implementing this for now) 
|| Layout (bits 13 to 20) The legth (L) of the Payload; (value == 0XXXXXXX) Payload can be 0 to 127 bytes wide; (value == 1XXXXXXX) suffix extended length one (LX1) to the header. Payload can be ~ 2^15 bytes wide; (value == 11XXXXXX) -> suffix extended length two (LX2) to the header. Payload can be ~ 2^23 bytes wide; ... (value == 1..n) -> sufix extended length n (LX(n)) to the header. Payload can be ~ 2^(7 + 8 * n) bytes wide. 
|| Layout (Bits 21 to 32): Key Index (KI)  is the index into another table that denotes tha actual key (managed externally from the MESSAGE). It shoul use the same extendable pattern as (L) 
|| Layout (bits depends on LX and KIX): The MAC is always 128 or 256 bits wide. The IV is always 96 or 128 bits wide. The Payload is len(L + LX) bytes wide. 

*/

    /*
----|0123456|70|123|45670123|456701234567|
    ----|----------------HEAD----------------|
    ----|-ECode-|-S|V--|----L---|-----KI-----|
    ----|------7|-9|-12|------20|----------32|
    ----|------7|-2|--3|-------8|----------12|
    ----|0123456|01|012|01234567|012345670123|
    ----|1111111|XX|XXX|XXXXXXXX|XXXXXXXXXXXX|
    _________________________________________.
    VX==NULL:V<>111--------------------------|
    =============================================.
        _________________________________________.
        LX==NULL:L<>1XXXXXXX---------------------|
        =========================================.
        _________________________________________.
        LX1<>NULL:L==1XXXXXXX--------------------|---LX1--|
        -----------------------------------------|-------8|
        -----------------------------------------|-------1|
        =========================================.
        _________________________________________.
        LX2<>NULL:L==11XXXXXX--------------------|-------LX2------|
        -----------------------------------------|--------------16|
        -----------------------------------------|-------1-------2|
        =========================================.
        _________________________________________.
        LX3<>NULL:L==111XXXXX--------------------|-----------LX3----------|
        -----------------------------------------|----------------------24|
        -----------------------------------------|-------1-------2-------3|
        =========================================.
        _________________________________________.
        LX4<>NULL:L==1111XXXX--------------------|--------------LX4---------------|
        -----------------------------------------|------------------------------32|
        -----------------------------------------|-------1-------2-------3-------4|
        =========================================.
        _________________________________________.
        KIX==NULL:KI<>1XXXXXXX-------------------|?-LX-?|
        =========================================.
        _________________________________________.
        KIX1<>NULL:KI==1XXXXXXX------------------|?-LX-?|--KIX1--|
        -----------------------------------------|?-LX-?|-------8|
        -----------------------------------------|?-LX-?|-------1|
        =========================================.
        _________________________________________.
        KIX2<>NULL:KI==11XXXXXX------------------|?-LX-?|------KIX2------|
        -----------------------------------------|?-LX-?|--------------16|
        -----------------------------------------|?-LX-?|-------1-------2|
        =========================================.
        _________________________________________.
        KIX3<>NULL:KI==111XXXXX------------------|?-LX-?|----------KIX3----------|
        -----------------------------------------|?-LX-?|----------------------24|
        -----------------------------------------|?-LX-?|-------1-------2-------3|
        =========================================.
        _________________________________________.
        KIX4<>NULL:KI==1111XXXX------------------|?-LX-?|-------------KIX4---------------|
        -----------------------------------------|?-LX-?|------------------------------32|
        -----------------------------------------|?-LX-?|-------1-------2-------3-------4|
        =========================================.
        _________________________________________.
        IV==96:S==00-----------------------------|?-LX-?|?-KIX-?|----------------------------------------IV00----------------------------------------------------|
        -----------------------------------------|?-LX-?|?-KIX-?|----------------------------------------------------------------------------------------------96|
        -----------------------------------------|?-LX-?|?-KIX-?|-------1-------2-------3-------4-------5-------6-------7-------8-------9------10------11------12|
        =========================================.
        _________________________________________.
        IV==128:S==01----------------------------|?-LX-?|?-KIX-?|----------------------------------------------------IV01------------------------------------------------------------------------|
        -----------------------------------------|?-LX-?|?-KIX-?|-----------------------------------------------------------------------------------------------------------------------------128|
        -----------------------------------------|?-LX-?|?-KIX-?|-------1-------2-------3-------4-------5-------6-------7-------8-------9------10------11------12------13-------14-----15------16|
        =========================================.
        _________________________________________.
        ?-LX-?|?-KIX-?|--IV(00 ? 01)--|----------------------------------------------------MAC / TAG-------------------------------------------------------------------|
        ?-LX-?|?-KIX-?|--IV(00 ? 01)--|-----------------------------------------------------------------------------------------------------------------------------128|
        ?-LX-?|?-KIX-?|--IV(00 ? 01)--|-------1-------2-------3-------4-------5-------6-------7-------8-------9------10------11------12------13-------14-----15------16|
        =========================================.
    _________________________________________.
    VX<>NULL:V==111--------------------------|(NOT IMPLEMENTED)
    ==========================================
*/
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

            // Reconstruct 32-bit word
            uint h = (uint)((span[0] << 24) | (span[1] << 16) | (span[2] << 8) | span[3]);
            _rawHeader = h;

            // 1. ECode (Bits 1-7)
            int eCode = (int)((h >> 25) & 0x7F); 
            if (eCode != 127)
                throw new InvalidOperationException($"Invalid Encoding Code: {eCode}. Expected 127.");

            // 2. S - Switch (Bits 8-9)
            // Controls both IV Width and MAC Width
            // S=00: IV=96 bits (12B), MAC=128 bits (16B) -> optimized for AES-GCM
            // S=01: IV=128 bits (16B), MAC=256 bits (32B) -> e.g. HMAC-SHA256 / AES-CBC
            int s = (int)((h >> 23) & 0x03);
            
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
                // Future use for S=2,3
                // Defaulting to "safe" large values or throwing? 
                // Let's treat as S=1 behavior for now to be safe, or throw.
                // Throwing is safer for explicit versioning.
                throw new NotSupportedException($"Switch value S={s} is not supported.");
            }

            // 3. V - Version (Bits 10-12)
            int v = (int)((h >> 20) & 0x07);
            if (v == 7) 
                throw new NotImplementedException("Extended header versioning not implemented.");

            // Track offset for variable fields
            int currentOffset = FixedHeaderSize;

            // 4. L - Payload Length (Bits 13-20)
            byte lRaw = (byte)((h >> 12) & 0xFF);
            PayloadLength = DecodeVariableLength(lRaw, data, ref currentOffset);

            // 5. KI - Key Index (Bits 21-32)
            ushort kiRaw = (ushort)(h & 0xFFF); 
            KeyIndex = DecodeVariableLength(kiRaw, data, ref currentOffset);

            // Calculate Offsets
            // Layout: [FixedHeader+Ext] [IV] [MAC] [Payload]
            IvOffset = currentOffset;
            MacOffset = IvOffset + IvLength;
            PayloadOffset = MacOffset + MacLength;
            TotalLength = PayloadOffset + PayloadLength;

            if (data.Length < TotalLength)
               throw new ArgumentException($"Data length {data.Length} is less than required {TotalLength}");
        }

        // Generic decoder for 8-bit base
        private static int DecodeVariableLength(byte baseValue, ReadOnlyMemory<byte> data, ref int offset)
        {
            // Count leading ones in the 8-bit value
            int extensions = 0;
            byte mask = 0x80;
            byte temp = baseValue;
            while ((temp & mask) != 0)
            {
                extensions++;
                temp = (byte)(temp << 1); 
                if (extensions >= 8) break; 
            }
            
            if (extensions == 0) return baseValue;

            // Remove prefix bits
            int value = baseValue & (0xFF >> (extensions + 1)); 

            // Read extension bytes
            var span = data.Span;
            for (int i = 0; i < extensions; i++)
            {
                if (offset >= data.Length) throw new IndexOutOfRangeException("Not enough data for extensions");
                value = (value << 8) | span[offset++];
            }
            return value;
        }

        // Overload for 12-bit base (Key Index)
        private static int DecodeVariableLength(ushort baseValue, ReadOnlyMemory<byte> data, ref int offset)
        {
            int extensions = 0;
            ushort mask = 0x800;
            ushort temp = baseValue;
            
            while ((temp & mask) != 0)
            {
                extensions++;
                temp = (ushort)(temp << 1);
            }

            if (extensions == 0) return baseValue;

            int keepBits = 12 - extensions - 1;
            int value = baseValue & ((1 << keepBits) - 1);

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