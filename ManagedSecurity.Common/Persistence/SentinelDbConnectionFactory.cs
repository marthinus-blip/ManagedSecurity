using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// A centralized factory for minting NativeAOT-compatible SQLite connections.
/// Implements WAL (Write-Ahead Logging) to prevent 'Database Is Locked' exceptions.
/// </summary>
public interface ISentinelDbConnectionFactory
{
    ISqlDialectTranslator Dialect { get; }
    Task<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}

public class SentinelDbConnectionFactory : ISentinelDbConnectionFactory
{
    public const string DefaultConnectionString = "Data Source=sentinel.db";
    public const string InitializationPragmaStatements = @"
                PRAGMA journal_mode = 'wal';
                PRAGMA synchronous = NORMAL;
                PRAGMA busy_timeout = 5000;
            ";

    private readonly string _connectionString;
    public ISqlDialectTranslator Dialect { get; }

    public SentinelDbConnectionFactory(string connectionString = DefaultConnectionString)
    {
        _connectionString = connectionString;
        Dialect = new SqliteDialectTranslator();
    }

    public async Task<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Enable Write-Ahead Logging for high concurrency without database locking
        using (var command = connection.CreateCommand())
        {
            command.CommandText = InitializationPragmaStatements;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return connection;
    }
}
