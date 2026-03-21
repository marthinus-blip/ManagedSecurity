using System;
using Microsoft.Extensions.Configuration;
using ManagedSecurity.Common.Persistence;

namespace ManagedSecurity.Common.Configuration;

/// <summary>
/// Architectural configuration source routing the Sentinel ADO.NET SQLite definitions 
/// seamlessly back into ASP.NET's IConfigurationRoot builder chain.
/// </summary>
public class SentinelDbConfigurationSource : IConfigurationSource
{
    public ISentinelDbConnectionFactory ConnectionFactory { get; set; } = null!;
    
    // Frequency at which PRAGMA data_version is sampled for out-of-band schema changes
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new SentinelDbConfigurationProvider(ConnectionFactory, PollingInterval);
    }
}
