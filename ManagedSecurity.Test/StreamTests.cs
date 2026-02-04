using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ManagedSecurity.Core;
using ManagedSecurity.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedSecurity.Test;

[TestClass]
public class StreamTests
{
    private class MockKeyProvider : IKeyProvider
    {
        private readonly byte[] _key = new byte[32];
        public MockKeyProvider() { for (int i = 0; i < 32; i++) _key[i] = (byte)i; }
        public ReadOnlyMemory<byte> GetKey(int keyIndex) => _key;
    }

    [TestMethod]
    public void Stream_Write_MultipleBlocks_Success()
    {
        var provider = new MockKeyProvider();
        var cipher = new Cipher(provider);
        using var ms = new MemoryStream();
        
        // Use a small chunk size to trigger multiple frames
        int chunkSize = 100;
        using (var cryptoStream = new ManagedSecurityStream(ms, cipher, ManagedSecurityStreamMode.Encrypt, chunkSize: chunkSize))
        {
            byte[] data = Encoding.UTF8.GetBytes(new string('A', 250)); // 2.5 blocks
            cryptoStream.Write(data);
        }

        byte[] encrypted = ms.ToArray();
        
        // Assertions
        // 1. Master Header (12 bytes)
        Assert.IsTrue(encrypted.Length > 12);
        Assert.AreEqual((byte)'M', encrypted[0]);
        
        // 2. Validate individual frames (S=2)
        // We expect 3 frames (100, 100, 50)
        var stream = new MemoryStream(encrypted);
        stream.Seek(14, SeekOrigin.Begin); // Master Header is now 14 bytes
        
        byte[] frameBuffer = new byte[encrypted.Length];
        int read = stream.Read(frameBuffer);
        
        // Check S=2
        // We need to re-parse the switch bit to be sure.
        uint raw = (uint)((frameBuffer[0] << 24) | (frameBuffer[1] << 16) | (frameBuffer[2] << 8) | frameBuffer[3]);
        int s = (int)((raw >> 25) & 0x03);
        Assert.AreEqual(2, s);
    }

    [TestMethod]
    public void Stream_RoundTrip_Success()
    {
        var provider = new MockKeyProvider();
        var cipher = new Cipher(provider);
        string original = "The quick brown fox jumps over the lazy dog. " + new string('!', 500);
        byte[] originalBytes = Encoding.UTF8.GetBytes(original);
        
        using var ms = new MemoryStream();
        
        // 1. Encrypt
        using (var encryptor = new ManagedSecurityStream(ms, cipher, ManagedSecurityStreamMode.Encrypt, chunkSize: 128))
        {
            encryptor.Write(originalBytes);
        }

        byte[] encrypted = ms.ToArray();
        
        // 2. Decrypt
        using var msDecrypt = new MemoryStream(encrypted);
        using var decryptor = new ManagedSecurityStream(msDecrypt, cipher, ManagedSecurityStreamMode.Decrypt);
        
        byte[] decryptedBytes = new byte[originalBytes.Length];
        int totalRead = 0;
        int read;
        while ((read = decryptor.Read(decryptedBytes, totalRead, decryptedBytes.Length - totalRead)) > 0)
        {
            totalRead += read;
        }

        Assert.AreEqual(originalBytes.Length, totalRead);
        string decrypted = Encoding.UTF8.GetString(decryptedBytes);
        Assert.AreEqual(original, decrypted);
    }
}
