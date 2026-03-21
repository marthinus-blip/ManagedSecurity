using Microsoft.Extensions.Configuration;
using ManagedSecurity.Common.Persistence;

namespace ManagedSecurity.Common.Configuration;

public static class SentinelDbExtensions
{
    /// <summary>
    /// Explicitly appends the NativeAOT ManagedSecurity Database Configuration Provider 
    /// over standard runtime variables, serving as the single source of truth 
    /// (replaces managed_cameras.json tracking).
    /// </summary>
    public static IConfigurationBuilder AddSentinelSqliteConfiguration(
        this IConfigurationBuilder builder, 
        ISentinelDbConnectionFactory factory)
    {
        return builder.Add(new SentinelDbConfigurationSource 
        { 
            ConnectionFactory = factory 
        });
    }
}
