using System.Collections.Generic;

namespace ManagedSecurity.Common.Security;

public interface IJwtTokenService
{
    /// <summary>
    /// Generates a signed Bearer Token encapsulating core authorizations natively.
    /// </summary>
    string GenerateToken(long userId, string email, long activeTenantId, short roleLevel);
}
