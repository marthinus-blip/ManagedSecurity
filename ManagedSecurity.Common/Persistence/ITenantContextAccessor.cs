namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// A scoped service interface designed to extract the active TenantId 
/// from the stateless JWT claims middleware pipeline natively.
/// </summary>
public interface ITenantContextAccessor
{
    /// <summary>
    /// Evaluates the ActiveTenantId required for PostgreSQL Row-Level Security injection natively.
    /// </summary>
    long GetActiveTenantId();
}
