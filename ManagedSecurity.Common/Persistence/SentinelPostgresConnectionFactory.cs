using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using ManagedSecurity.Common.Attributes;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// A centralized factory for minting NativeAOT-compatible PostgreSQL connections for com_proj.
/// Implements Row-Level Security (RLS) enforcement strictly upon socket leasing.
/// </summary>
[AllowMagicValues]
public class SentinelPostgresConnectionFactory : ISentinelDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public ISqlDialectTranslator Dialect { get; }

    public SentinelPostgresConnectionFactory(string connectionString, ITenantContextAccessor tenantContextAccessor)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _tenantContextAccessor = tenantContextAccessor ?? throw new ArgumentNullException(nameof(tenantContextAccessor));
        Dialect = new PostgresDialectTranslator();
    }

    public async Task<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Security Boundary: Extract the active Tenant ID bound to this execution context.
        long activeTenantId = _tenantContextAccessor.GetActiveTenantId();

        // Enforce Row-Level Security natively BEFORE yielding the generic socket back to ADO.NET.
        // Npgsql supports parameterizing SET LOCAL safely if structured correctly, but it's often significantly faster
        // and completely mathematically safe natively since activeTenantId is a strictly typed Int64.
        using (var command = connection.CreateCommand())
        {
            // Inject the Int64 value directly. This establishes the Zero-Trust multi-tenant isolation constraints for the physical session locally.
            command.CommandText = $"SET app.current_tenant_id = '{activeTenantId}';";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return connection;
    }
}
