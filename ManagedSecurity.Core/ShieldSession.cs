using System.Security.Cryptography;
using System.Buffers.Binary;
using ManagedSecurity.Common;

namespace ManagedSecurity.Core;

/// <summary>
/// Implements a lightweight, zero-allocation handshake to establish 
/// a secure session between a Camera (Producer) and a Hub (Consumer).
/// </summary>
public sealed class ShieldSession : IDisposable
{
    private static ReadOnlySpan<byte> HandshakeMagic => "SHLD"u8;
    private const byte HandshakeVersion = 1;

    private readonly ECDiffieHellman _ecdh;
    private readonly byte[] _publicKey;

    public ShieldSession()
    {
        // Use X25519 for modern, fast, and high-security key exchange
        // Fallback to P256 if X25519 is not supported on the host OS
        _ecdh = CreateDiffieHellman();
        _publicKey = _ecdh.ExportSubjectPublicKeyInfo();
    }

    private static ECDiffieHellman CreateDiffieHellman()
    {
        try { return ECDiffieHellman.Create(ECCurve.CreateFromFriendlyName("x25519")); }
        catch { return ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256); }
    }

    public async Task<byte[]> PerformHandshakeAsync(Stream stream, CancellationToken ct = default)
    {
        // 1. Send our Handshake Header + Public Key
        // [Magic 4B] [Ver 1B] [KeyLen 2B] [PublicKey N]
        byte[] outgoing = new byte[7 + _publicKey.Length];
        HandshakeMagic.CopyTo(outgoing);
        outgoing[4] = HandshakeVersion;
        BinaryPrimitives.WriteUInt16BigEndian(outgoing.AsSpan(5), (ushort)_publicKey.Length);
        _publicKey.CopyTo(outgoing.AsSpan(7));

        await stream.WriteAsync(outgoing, ct);
        await stream.FlushAsync(ct);

        // 2. Read Peer Handshake
        byte[] header = new byte[7];
        await ReadExactAsync(stream, header, ct);

        if (!header.AsSpan(0, 4).SequenceEqual(HandshakeMagic))
            throw new CryptographicException("Invalid handshake magic.");
        
        if (header[4] != HandshakeVersion)
            throw new NotSupportedException($"Unsupported handshake version: {header[4]}");

        ushort peerKeyLen = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(5));
        byte[] peerKey = new byte[peerKeyLen];
        await ReadExactAsync(stream, peerKey, ct);

        // 3. Derive Shared Secret
        using var peerPublic = ECDiffieHellman.Create();
        peerPublic.ImportSubjectPublicKeyInfo(peerKey, out _);
        
        // Derive raw secret for consistent HKDF input across platforms
        byte[] sharedSecret = _ecdh.DeriveRawSecretAgreement(peerPublic.PublicKey);

        // 4. Use HKDF-SHA256 to derive a 256-bit session key
        byte[] key = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, info: "ManagedSecurity_Session_V1"u8.ToArray());
        Console.WriteLine($"[DEBUG] Session Key Hash: {BitConverter.ToString(key, 0, 4)}");
        return key;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead), ct);
            if (read == 0) throw new EndOfStreamException("Handshake truncated.");
            totalRead += read;
        }
    }

    public void Dispose() => _ecdh.Dispose();
}
