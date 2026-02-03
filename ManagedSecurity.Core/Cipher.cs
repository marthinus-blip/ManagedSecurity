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

        public int GetRequiredSize(int plaintextLength, int keyIndex, bool highSecurity = false)
        {
             return Bindings.Header.GetRequiredSize(plaintextLength, keyIndex, highSecurity);
        }

        public byte[] Encrypt(ReadOnlySpan<byte> plaintext, int keyIndex, bool highSecurity = false)
        {
            byte[] buffer = new byte[GetRequiredSize(plaintext.Length, keyIndex, highSecurity)];
            Encrypt(plaintext, buffer, keyIndex, highSecurity);
            return buffer;
        }

        public void Encrypt(ReadOnlySpan<byte> plaintext, Span<byte> destination, int keyIndex, bool highSecurity = false)
        {
            if (highSecurity)
                EncryptS1(plaintext, destination, keyIndex);
            else
                EncryptS0(plaintext, destination, keyIndex);
        }

        private void EncryptS0(ReadOnlySpan<byte> plaintext, Span<byte> destination, int keyIndex)
        {
            var key = _keyProvider.GetKey(keyIndex);
            const int MacLength = 16;
            
            Bindings.Header.Write(destination, plaintext.Length, keyIndex, false);
            var h = new Bindings.Header(destination);
            
            RandomNumberGenerator.Fill(destination.Slice(h.IvOffset, h.IvLength));

            using var aes = new AesGcm(key.Span, MacLength);
            
            var nonce = h.GetIv(destination);
            var ciphertextDst = h.GetPayload(destination);
            var tagDst = h.GetMac(destination);
            var associatedData = destination.Slice(0, h.IvOffset);

            aes.Encrypt(nonce, plaintext, ciphertextDst, tagDst, associatedData);
        }

        private void EncryptS1(ReadOnlySpan<byte> plaintext, Span<byte> destination, int keyIndex)
        {
            // S=1: AES-GCM-SIV (Synthetic IV via HMAC-SHA256)
            var masterKey = _keyProvider.GetKey(keyIndex);
            
            Bindings.Header.Write(destination, plaintext.Length, keyIndex, true);
            var h = new Bindings.Header(destination);
            
            // 1. Generate Input Nonce (16 bytes)
            var inputNonce = h.GetIv(destination);
            RandomNumberGenerator.Fill(inputNonce);

            // 2. Derive Subkeys (Encryption & Authentication)
            Span<byte> prk = stackalloc byte[32];
            HKDF.Extract(HashAlgorithmName.SHA256, masterKey.Span, inputNonce, prk);
            
            Span<byte> encKey = stackalloc byte[32];
            Span<byte> authKey = stackalloc byte[32];
            HKDF.Expand(HashAlgorithmName.SHA256, prk, encKey, "ManagedSecurity S1 ENC"u8);
            HKDF.Expand(HashAlgorithmName.SHA256, prk, authKey, "ManagedSecurity S1 MAC"u8);

            // 3. Compute Synthetic Tag (SIV)
            // We use HMAC to derive the IV from the plaintext for nonce-misuse resistance.
            var associatedData = destination.Slice(0, h.IvOffset + h.IvLength);
            var tagDst = h.GetMac(destination);
            
            Span<byte> hmacFull = stackalloc byte[32];
            IncrementalHash hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, authKey);
            hmac.AppendData(associatedData);
            hmac.AppendData(plaintext);
            hmac.GetHashAndReset(hmacFull);

            // 4. Encrypt with Synthetic IV (First 12 bytes of HMAC)
            var iv = hmacFull.Slice(0, 12);
            var ciphertextDst = h.GetPayload(destination);
            
            using var aes = new AesGcm(encKey, 16);
            Span<byte> gcmTag = stackalloc byte[16];
            
            aes.Encrypt(iv, plaintext, ciphertextDst, gcmTag, associatedData);

            // 5. Store combined Tag: [16 bytes GCM Tag][16 bytes HMAC]
            gcmTag.CopyTo(tagDst.Slice(0, 16));
            hmacFull.Slice(0, 16).CopyTo(tagDst.Slice(16, 16));
        }

        public byte[] Decrypt(ReadOnlySpan<byte> message)
        {
            var h = new Bindings.Header(message);
            byte[] plaintext = new byte[h.PayloadLength];
            Decrypt(message, plaintext);
            return plaintext;
        }

        public void Decrypt(ReadOnlySpan<byte> message, Span<byte> destination)
        {
            var h = new Bindings.Header(message);
            
            if (h.IvLength == 12 && h.MacLength == 16)
            {
                DecryptS0(h, message, destination);
            }
            else if (h.IvLength == 16 && h.MacLength == 32)
            {
                DecryptS1(h, message, destination);
            }
            else
            {
                throw new NotSupportedException($"Unsupported header profile: IV={h.IvLength}, MAC={h.MacLength}");
            }
        }

        private void DecryptS0(Bindings.Header h, ReadOnlySpan<byte> message, Span<byte> destination)
        {
            var key = _keyProvider.GetKey(h.KeyIndex);
            using var aes = new AesGcm(key.Span, h.MacLength);

            var nonce = h.GetIv(message);
            var ciphertext = h.GetPayload(message);
            var tag = h.GetMac(message);
            var associatedData = message.Slice(0, h.IvOffset);

            aes.Decrypt(nonce, ciphertext, tag, destination, associatedData);
        }

        private void DecryptS1(Bindings.Header h, ReadOnlySpan<byte> message, Span<byte> destination)
        {
            var masterKey = _keyProvider.GetKey(h.KeyIndex);
            var inputNonce = h.GetIv(message);

            // 1. Derive Subkeys
            Span<byte> prk = stackalloc byte[32];
            HKDF.Extract(HashAlgorithmName.SHA256, masterKey.Span, inputNonce, prk);

            Span<byte> encKey = stackalloc byte[32];
            Span<byte> authKey = stackalloc byte[32];
            HKDF.Expand(HashAlgorithmName.SHA256, prk, encKey, "ManagedSecurity S1 ENC"u8);
            HKDF.Expand(HashAlgorithmName.SHA256, prk, authKey, "ManagedSecurity S1 MAC"u8);

            // 2. Extract Combined Tag
            var combinedTag = h.GetMac(message);
            var gcmTag = combinedTag.Slice(0, 16);
            var hmacStored = combinedTag.Slice(16, 16);
            
            // 3. Reconstruct IV from stored HMAC part
            var iv = hmacStored.Slice(0, 12);
            var ciphertext = h.GetPayload(message);
            var associatedData = message.Slice(0, h.IvOffset + h.IvLength);

            using var aes = new AesGcm(encKey, 16);
            
            // 4. Decrypt and validate GCM Tag
            aes.Decrypt(iv, ciphertext, gcmTag, destination, associatedData);

            // 5. Validate HMAC (Second layer of protection)
            Span<byte> hmacComputed = stackalloc byte[32];
            IncrementalHash hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, authKey);
            hmac.AppendData(associatedData);
            hmac.AppendData(destination);
            hmac.GetHashAndReset(hmacComputed);

            if (!CryptographicOperations.FixedTimeEquals(hmacComputed.Slice(0, 16), hmacStored))
            {
                CryptographicOperations.ZeroMemory(destination);
                throw new CryptographicException("SIV HMAC Authentication failed.");
            }
        }
        
        // Overload to avoid copy if caller has Memory
        public byte[] Decrypt(byte[] message)
        {
             return Decrypt(new ReadOnlyMemory<byte>(message));
        }

        public byte[] Decrypt(ReadOnlyMemory<byte> message)
        {
             return Decrypt(message.Span);
        }
    }
}
