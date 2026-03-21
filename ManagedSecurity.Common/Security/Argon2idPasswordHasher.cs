using System;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using ManagedSecurity.Common.Attributes;

namespace ManagedSecurity.Common.Security;

[AllowMagicValues]
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    // High-security constraints mirroring OWASP recommendations.
    private const int MemoryCostKb = 65536; // 64 MB
    private const int Iterations = 4;
    private const int Parallelism = 2;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string HashPassword(string password)
    {
        byte[] salt = new byte[SaltSize];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = Parallelism,
            Iterations = Iterations,
            MemorySize = MemoryCostKb
        };

        byte[] hash = argon2.GetBytes(HashSize);

        // Store configuration parameters structurally to allow future rotation seamlessly
        return $"{MemoryCostKb}${Iterations}${Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string storedHash, string password)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 5) return false;

        if (!int.TryParse(parts[0], out int memCost) ||
            !int.TryParse(parts[1], out int iterations) ||
            !int.TryParse(parts[2], out int parallelism))
        {
            return false;
        }

        byte[] salt = Convert.FromBase64String(parts[3]);
        byte[] expectedHash = Convert.FromBase64String(parts[4]);

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = parallelism,
            Iterations = iterations,
            MemorySize = memCost
        };

        byte[] actualHash = argon2.GetBytes(expectedHash.Length);
        
        // Cryptographically constant-time comparison securely mapping boundaries 
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    public void ExecuteDummyValidation()
    {
        // Deterministic dummy hash against a dead salt natively.
        // Replicates identical compute overhead structurally.
        byte[] dummySalt = new byte[SaltSize];
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes("sentinel_dummy"))
        {
            Salt = dummySalt,
            DegreeOfParallelism = Parallelism,
            Iterations = Iterations,
            MemorySize = MemoryCostKb
        };
        _ = argon2.GetBytes(HashSize);
    }
}
