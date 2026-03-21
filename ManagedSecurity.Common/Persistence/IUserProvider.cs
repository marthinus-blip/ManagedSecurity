namespace ManagedSecurity.Common.Persistence;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Defines the Dependency Inversion boundary for the Global Identity layer natively.
/// Controllers must never instantiate SQL strings directly, instead relying on these deterministic interface abstractions.
/// </summary>
public interface IUserProvider
{
    Task<UserRecord?> GetUserByEmailAsync(string emailAddress, CancellationToken cancellationToken = default);
    Task<long> CreateUserAsync(UserRecord record, CancellationToken cancellationToken = default);
}
