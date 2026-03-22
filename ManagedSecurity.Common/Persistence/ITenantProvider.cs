namespace ManagedSecurity.Common.Persistence;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Defines the Dependency Inversion boundary for Multi-Tenant Persona mapping.
/// </summary>
public interface ITenantProvider
{
    /// <summary>
    /// Executes the Many-to-Many query for populating the Dashboard's Authentication "Tenant Switcher" UI.
    /// Used natively prior to the issuance of a scoped ActiveTenant JWT.
    /// </summary>
    Task<IReadOnlyList<TenantRecord>> GetAuthorizedTenantsForUserAsync(long userId, CancellationToken cancellationToken = default);
    
    Task<long> CreateTenantAsync(TenantRecord record, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Generates the Contextual Role relationship (The Persona) inside the Database junction table.
    /// </summary>
    Task GrantTenantAccessAsync(TenantUserAccessRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates the Many-to-Many junction unpacking physical capability structures natively dynamically.
    /// </summary>
    Task<IReadOnlyList<string>> GetUserCapabilitiesForTenantAsync(long userId, long tenantId, CancellationToken cancellationToken = default);
}
