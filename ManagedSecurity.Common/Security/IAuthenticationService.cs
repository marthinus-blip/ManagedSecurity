using System.Threading;
using System.Threading.Tasks;

namespace ManagedSecurity.Common.Security;

/// <summary>
/// Orchestrates rigorous zero-trust authentication protocols, binding Identity mapping and Tenant boundaries cleanly.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Authenticates a given identity and generates a cryptographically signed Bearer Token.
    /// Incorporates deterministic memory-hard dummy verification constraints mathematically foiling enumeration.
    /// </summary>
    Task<string?> AuthenticateAsync(string emailAddress, string plainTextPassword, long? targetTenantId = null, CancellationToken cancellationToken = default);
}
