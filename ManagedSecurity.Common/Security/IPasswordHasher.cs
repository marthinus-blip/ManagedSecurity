namespace ManagedSecurity.Common.Security;

/// <summary>
/// A zero-allocation provider mapping memory-hard hashing functions explicitly to prevent offline brute-force and enumeration bounds.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes a raw password leveraging Argon2id.
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    /// Validates a raw password against the derived Argon2id hash natively.
    /// </summary>
    bool VerifyPassword(string hash, string password);

    /// <summary>
    /// Executes a constant-time dummy validation routine neutralizing side-channel enumeration attacks natively.
    /// </summary>
    void ExecuteDummyValidation();
}
