using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ManagedSecurity.Common.Attributes;
using ManagedSecurity.Common.Persistence;

namespace ManagedSecurity.Common.Security;

[AllowMagicValues]
public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IUserProvider _userProvider;
    private readonly ITenantProvider _tenantProvider;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthenticationService(
        IUserProvider userProvider,
        ITenantProvider tenantProvider,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService)
    {
        _userProvider = userProvider ?? throw new ArgumentNullException(nameof(userProvider));
        _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _jwtTokenService = jwtTokenService ?? throw new ArgumentNullException(nameof(jwtTokenService));
    }

    public async Task<string?> AuthenticateAsync(string emailAddress, string plainTextPassword, long? targetTenantId = null, CancellationToken cancellationToken = default)
    {
        var userRecord = await _userProvider.GetUserByEmailAsync(emailAddress, cancellationToken).ConfigureAwait(false);

        if (userRecord == null)
        {
            // The user does not exist. However, returning immediately creates a timing oracle.
            // We invoke the constant-time dummy cycle to normalize response duration transparently.
            _passwordHasher.ExecuteDummyValidation();
            return null;
        }

        bool isValid = _passwordHasher.VerifyPassword(userRecord.Value.PasswordHash, plainTextPassword);
        if (!isValid)
        {
            return null;
        }

        // Identity confirmed natively. Verify contextual constraints dynamically.
        var authorizedTenants = await _tenantProvider.GetAuthorizedTenantsForUserAsync(userRecord.Value.UserId, cancellationToken).ConfigureAwait(false);

        if (authorizedTenants.Count == 0)
        {
            // Identity holds no active boundaries natively.
            return null;
        }

        long activeTenantId;
        if (targetTenantId.HasValue)
        {
            bool authorizedForTarget = authorizedTenants.Any(t => t.TenantId == targetTenantId.Value);
            if (!authorizedForTarget)
            {
                return null;
            }
            activeTenantId = targetTenantId.Value;
        }
        else
        {
            // Default to the first chronological Tenant natively.
            activeTenantId = authorizedTenants[0].TenantId;
        }

        // We assume RoleLevel could vary per Tenant in the future, but currently we pass an implicit operator gracefully.
        short explicitRoleLevel = 2; // Assuming 2 = Operator / Standard constraints.

        string signedToken = _jwtTokenService.GenerateToken(userRecord.Value.UserId, userRecord.Value.EmailAddress, activeTenantId, explicitRoleLevel);

        return signedToken;
    }
}
