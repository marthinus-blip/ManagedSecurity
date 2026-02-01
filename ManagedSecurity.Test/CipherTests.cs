using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ManagedSecurity.Core;
using ManagedSecurity.Common;

namespace ManagedSecurity.Test;

[TestClass]
public class CipherTests
{
    private class MockKeyProvider : IKeyProvider
    {
        private readonly byte[] _key = new byte[32]; // 256-bit

        public MockKeyProvider()
        {
            // Deterministic key for testing
            for (int i = 0; i < _key.Length; i++) _key[i] = (byte)i;
        }

        public ReadOnlyMemory<byte> GetKey(int keyIndex) => _key;
    }

    [TestMethod, Priority(1), TestCategory("RoundTrip")]
    public void Encrypt_Decrypt_RoundTrip_Success()
    {
        var provider = new MockKeyProvider();
        var cipher = new Cipher(provider);
        
        string secretText = "Hello NativeAOT World!";
        byte[] plain = Encoding.UTF8.GetBytes(secretText);
        int keyIndex = 123; // Test non-zero index

        // 1. Encrypt
        byte[] encryptedMessage = cipher.Encrypt(plain, keyIndex);

        // Verify Header is valid structure
        var h = new Bindings.Header(encryptedMessage);
        Assert.AreEqual(plain.Length, h.PayloadLength);
        Assert.AreEqual(keyIndex, h.KeyIndex);
        Assert.AreEqual(12, h.IvLength); // GCM Default

        // 2. Decrypt
        byte[] decryptedBytes = cipher.Decrypt(encryptedMessage);
        string decryptedText = Encoding.UTF8.GetString(decryptedBytes);

        Assert.AreEqual(secretText, decryptedText);
    }

    [TestMethod]
    public void Decrypt_TamperedHeader_Throws()
    {
        var provider = new MockKeyProvider();
        var cipher = new Cipher(provider);
        
        byte[] plain = Encoding.UTF8.GetBytes("Sensitive Data");
        byte[] msg = cipher.Encrypt(plain, 1);

        // Tamper with the Version or PayloadLength in the header
        // Header bytes are at start.
        msg[3] ^= 0xFF; // Flip bits in the last byte of fixed header

        // Because we use Associated Data (AAD), this must fail authentication!
        Assert.ThrowsException<System.Security.Cryptography.AuthenticationTagMismatchException>(() => 
        {
            cipher.Decrypt(msg);
        });
    }
}
