using BenchmarkDotNet.Attributes;
using ManagedSecurity.Core;
using ManagedSecurity.Common;
using System.Security.Cryptography;

namespace ManagedSecurity.Benchmarks;

[MemoryDiagnoser]
public class CipherBenchmarks
{
    private Cipher _cipher = null!;
    private AesGcm _rawAes = null!;
    private byte[] _key = null!;
    private byte[] _plaintext = null!;
    private byte[] _encryptedManaged = null!;
    private byte[] _nonce = null!;
    private byte[] _tag = null!;
    private byte[] _ciphertext = null!;
    private byte[] _packetBuffer = null!;

    [Params(128, 1024 * 64)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _key = new byte[32];
        RandomNumberGenerator.Fill(_key);
        _plaintext = new byte[PayloadSize];
        RandomNumberGenerator.Fill(_plaintext);

        _cipher = new Cipher(new ConstantKeyProvider(_key));
        _rawAes = new AesGcm(_key, 16);

        // Pre-allocate for raw comparison to show 0-alloc baseline
        _nonce = new byte[12];
        _tag = new byte[16];
        _ciphertext = new byte[PayloadSize];
        
        // Pre-allocate packet for ManagedSecurity Zero-Alloc test
        _packetBuffer = new byte[_cipher.GetRequiredSize(PayloadSize, 0, 0)];
        
        // Pre-allocate for decryption test
        _encryptedManaged = _cipher.Encrypt(_plaintext, 0);
    }

    [Benchmark(Baseline = true, Description = "Raw AesGcm (No Alloc)")]
    public void RawAesGcm_Roundtrip()
    {
        // This is a pure crypto baseline
        _rawAes.Encrypt(_nonce, _plaintext, _ciphertext, _tag);
        _rawAes.Decrypt(_nonce, _ciphertext, _tag, _plaintext);
    }

    [Benchmark(Description = "ManagedSecurity (Current)")]
    public void ManagedSecurity_Roundtrip()
    {
        // Currently ManagedSecurity.Cipher allocates byte[] for both Encrypt and Decrypt
        byte[] encrypted = _cipher.Encrypt(_plaintext, 0);
        byte[] decrypted = _cipher.Decrypt(encrypted);
    }

    [Benchmark(Description = "ManagedSecurity (Zero Alloc)")]
    public void ManagedSecurity_ZeroAlloc_Roundtrip()
    {
        // Using the new Span-based API to show 0 allocations
        _cipher.Encrypt(_plaintext, _packetBuffer, 0);
        _cipher.Decrypt(_packetBuffer, _plaintext);
    }

    private class ConstantKeyProvider : IKeyProvider
    {
        private readonly ReadOnlyMemory<byte> _key;
        public ConstantKeyProvider(byte[] key) => _key = key;
        public ReadOnlyMemory<byte> GetKey(int keyIndex) => _key;
    }
}

[MemoryDiagnoser]
public class HeaderBenchmarks
{
    private byte[] _headerData = null!;
    private byte[] _extendedHeaderData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _headerData = new byte[Bindings.Header.GetRequiredSize(100, 1, 0)];
        Bindings.Header.Write(_headerData, 100, 1, 0);

        // 1MB payload triggers 1-byte extension for L
        _extendedHeaderData = new byte[Bindings.Header.GetRequiredSize(1024 * 1024, 1, 0)];
        Bindings.Header.Write(_extendedHeaderData, 1024 * 1024, 1, 0);
    }

    [Benchmark(Baseline = true, Description = "Header Parse (Standard)")]
    public Bindings.Header ParseStandard()
    {
        return new Bindings.Header(_headerData);
    }

    [Benchmark(Description = "Header Parse (Extended)")]
    public Bindings.Header ParseExtended()
    {
        return new Bindings.Header(_extendedHeaderData);
    }
}
