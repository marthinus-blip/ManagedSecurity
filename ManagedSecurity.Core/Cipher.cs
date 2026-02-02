using System;
using System.Security.Cryptography;
using ManagedSecurity.Common;

namespace ManagedSecurity.Core
{
    public class Cipher
    {
        private readonly IKeyProvider _keyProvider;

        public Cipher(IKeyProvider keyProvider)
        {
            _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        }

        /// <summary>
        /// Encrypts plaintext using AES-GCM (S=0 profile).
        /// </summary>
        /// <returns>A new byte array containing the full binary message (Header + IV + MAC + Ciphertext).</returns>
        public byte[] Encrypt(ReadOnlySpan<byte> plaintext, int keyIndex)
        {
            // 1. Fetch Key
            var key = _keyProvider.GetKey(keyIndex);
            
            // 2. Validate Profile (S=0 -> GCM)
            // IV=12 bytes (96 bits)
            const int IvLength = 12;
            const int MacLength = 16;
            bool highSecurity = false; // S=0

            // 3. Calculate Size
            int requiredSize = Bindings.Header.GetRequiredSize(plaintext.Length, keyIndex, highSecurity);
            byte[] buffer = new byte[requiredSize];

            // 4. Write Header
            Bindings.Header.Write(buffer, plaintext.Length, keyIndex, highSecurity);

            // 5. Generate IV
            // The writer logic puts IV immediately after the header+extensions.
            // We need to parse our own buffer (or calculate offset) to find where to put IV.
            // Optimization: We could have Header.Write return the payload offset or lengths.
            // For now, let's just re-parse the header we just wrote to find precise offsets. 
            // It's cheap (zero copy).
            var h = new Bindings.Header(buffer);
            
            // h.IvOffset is where IV starts.
            // h.Iv is a span we can fill.
            RandomNumberGenerator.Fill(buffer.AsSpan(h.IvOffset, IvLength));

            // 6. Encrypt
            // AesGcm(key)
            using var aes = new AesGcm(key.Span, MacLength);
            
            // Inputs:
            var nonce = h.GetIv(buffer); // IV
            
            // Outputs:
            var ciphertextDst = h.GetPayload(buffer);
            var tagDst = h.GetMac(buffer);
            // Wait, does AesGcm write MAC to tagDst? 
            // Yes: Encrypt(nonce, plaintext, ciphertext, tag, associatedData)

            // Auth Data: We should authenticate the header so it can't be tampered with!
            // The header bits (Header + Extensions) are at 0..IvOffset.
            var associatedData = buffer.AsSpan(0, h.IvOffset);

            aes.Encrypt(
                nonce,
                plaintext,
                ciphertextDst,
                tagDst,
                associatedData
            );

            return buffer;
        }

        /// <summary>
        /// Decrypts a full binary message.
        /// </summary>
        /// <returns>Plaintext byte array.</returns>
        public byte[] Decrypt(ReadOnlySpan<byte> message)
        {
            // 1. Parse Header (Zero Allocation)
            var h = new Bindings.Header(message);
            
            // 2. Extract Key
            var key = _keyProvider.GetKey(h.KeyIndex);

            // 3. Setup AES
            if (h.IvLength != 12 || h.MacLength != 16)
                throw new NotSupportedException("Only S=0 (AES-GCM) is supported currently.");

            using var aes = new AesGcm(key.Span, h.MacLength);

            // 4. Decrypt
            var nonce = h.GetIv(message);
            var ciphertext = h.GetPayload(message);
            var tag = h.GetMac(message);
            var associatedData = message.Slice(0, h.IvOffset); // Authenticate Header!

            // Allocate result
            byte[] plaintext = new byte[h.PayloadLength];

            aes.Decrypt(
                nonce,
                ciphertext,
                tag,
                plaintext,
                associatedData
            );

            return plaintext;
        }
        
        // Overload to avoid copy if caller has Memory
        public byte[] Decrypt(byte[] message)
        {
             return Decrypt(new ReadOnlyMemory<byte>(message));
        }

        public byte[] Decrypt(ReadOnlyMemory<byte> message)
        {
             var h = new Bindings.Header(message.Span);
             var key = _keyProvider.GetKey(h.KeyIndex);
             
             if (h.IvLength != 12 || h.MacLength != 16)
                throw new NotSupportedException("Only S=0 (AES-GCM) is supported currently.");

             using var aes = new AesGcm(key.Span, h.MacLength);
             
             // Span slicing from Memory (Zero Allocation)
             var nonce = h.GetIv(message.Span); 
             var ciphertext = h.GetPayload(message.Span);
             var tag = h.GetMac(message.Span);
             var associatedData = message.Span.Slice(0, h.IvOffset);

             byte[] plaintext = new byte[h.PayloadLength];
             
             aes.Decrypt(
                nonce,
                ciphertext,
                tag,
                plaintext,
                associatedData
             );

             return plaintext;
        }
    }
}
