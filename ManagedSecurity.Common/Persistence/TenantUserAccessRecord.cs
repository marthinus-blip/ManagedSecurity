namespace ManagedSecurity.Common.Persistence;

public struct TenantUserAccessRecord
{
    public const string SchemaNameQl = "auth";
    public const string TableNameQl = "TenantUserAccess";

    public long TenantId { get; set; }
    public long UserId { get; set; }
    
    /// <summary>
    /// Defines the authorization boundary dynamically. 
    /// Typical spans: 0=Viewer, 100=Operator, 255=Administrator.
    /// Maps cleanly into PostgreSQL SMALLINT (Int16/short).
    /// </summary>
    public short RoleLevel { get; set; } 
    
    public long GrantedAtEpoch { get; set; }
}
