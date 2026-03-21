namespace ManagedSecurity.Common.Persistence;

public struct UserRecord
{
    public long UserId { get; set; }
    
    public string EmailAddress { get; set; }
    public string PasswordHash { get; set; }
    
    /// <summary>
    /// Changes when the password is reset natively. Allows the architecture to instantaneously 
    /// invalidate all active stateless JWTs or WebSocket sessions.
    /// </summary>
    public string SecurityStamp { get; set; } 
    
    public bool IsDeleted { get; set; }
    
    public long CreatedAtEpoch { get; set; }
    public long UpdatedAtEpoch { get; set; }
    public long UpdatedByUserId { get; set; }
}
