namespace ManagedSecurity.Common.Persistence;

public struct TenantRecord
{
    public const string SchemaNameQl = "auth";
    public const string TableNameQl = "Tenants";

    public long TenantId { get; set; }
    
    public string OrganizationName { get; set; }
    public bool IsDeleted { get; set; }
    
    public long CreatedAtEpoch { get; set; }
    public long UpdatedAtEpoch { get; set; }
    public long UpdatedByUserId { get; set; }
}
