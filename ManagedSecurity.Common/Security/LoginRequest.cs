namespace ManagedSecurity.Common.Security;

public record LoginRequest(string EmailAddress, string Password, long? TenantId = null);
