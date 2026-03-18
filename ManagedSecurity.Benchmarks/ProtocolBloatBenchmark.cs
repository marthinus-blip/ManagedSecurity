using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using ManagedSecurity.Core;
using ManagedSecurity.Common;

namespace ManagedSecurity.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[MarkdownExporterAttribute.GitHub]
public class ProtocolBloatBenchmark
{
    private Cipher _cipher = null!;
    private byte[] _payload = null!;
    private byte[] _s0Buffer = null!;
    private byte[] _s1Buffer = null!;
    private byte[] _s2Buffer = null!;

    [Params(64, 1024, 65536, 1048576)] // 64B, 1KB, 64KB, 1MB
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var keyProvider = new StaticKeyProvider(new byte[32]);
        _cipher = new Cipher(keyProvider);

        _payload = new byte[PayloadSize];
        Random.Shared.NextBytes(_payload);

        _s0Buffer = new byte[_cipher.GetRequiredSize(PayloadSize, 0, 0)];
        _s1Buffer = new byte[_cipher.GetRequiredSize(PayloadSize, 0, 1)];
        _s2Buffer = new byte[_cipher.GetRequiredSize(PayloadSize, 0, 2)];

        // Pre-run to compute EXACT empirical bloat for logs
        _cipher.Encrypt(_payload, _s0Buffer, 0, 0);
        _cipher.Encrypt(_payload, _s1Buffer, 0, 1);
        _cipher.Encrypt(_payload, _s2Buffer, 0, 2, sequenceNumber: 1234);

        Console.WriteLine($"\n| [Empirical Bloat Record - Payload: {PayloadSize:N0} Bytes] |");
        Console.WriteLine("| Profile | Output Bytes | Overhead (Bytes) | Bloat % |");
        Console.WriteLine("|---------|--------------|------------------|---------|");
        Console.WriteLine($"| S=0     | {_s0Buffer.Length,-12} | {_s0Buffer.Length - PayloadSize,-16} | {((float)(_s0Buffer.Length - PayloadSize) / PayloadSize * 100):F4}% |");
        Console.WriteLine($"| S=1     | {_s1Buffer.Length,-12} | {_s1Buffer.Length - PayloadSize,-16} | {((float)(_s1Buffer.Length - PayloadSize) / PayloadSize * 100):F4}% |");
        Console.WriteLine($"| S=2     | {_s2Buffer.Length,-12} | {_s2Buffer.Length - PayloadSize,-16} | {((float)(_s2Buffer.Length - PayloadSize) / PayloadSize * 100):F4}% |");
        Console.WriteLine();
    }

    [Benchmark(Description = "S=0 (Standard AES-GCM)")]
    public void MeasureS0_SpeedAndAlloc() => _cipher.Encrypt(_payload, _s0Buffer, 0, 0);

    [Benchmark(Description = "S=1 (High-Security)")]
    public void MeasureS1_SpeedAndAlloc() => _cipher.Encrypt(_payload, _s1Buffer, 0, 1);

    [Benchmark(Description = "S=2 (Streaming API)")]
    public void MeasureS2_SpeedAndAlloc() => _cipher.Encrypt(_payload, _s2Buffer, 0, 2, sequenceNumber: 1234);

    private class StaticKeyProvider : IKeyProvider
    {
        private readonly byte[] _key;
        public StaticKeyProvider(byte[] key) => _key = key;
        public ReadOnlyMemory<byte> GetKey(int keyIndex) => _key;
    }
}
