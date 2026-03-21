using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ManagedSecurity.Common.Attributes;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// Provides atomic distributed locking mechanisms over native SQLite using Write-Ahead Logging.
/// Escapes external Redis dependencies for Orchestrator task execution natively.
/// </summary>
[AllowMagicValues]
public sealed class SentinelDbJobLeaseProvider : IJobLeaseProvider
{
    private readonly ISentinelDbConnectionFactory _connectionFactory;

    public SentinelDbJobLeaseProvider(ISentinelDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Attempts to securely acquire an atomic lease for a defined duration on the hot path.
    /// Exists gracefully leveraging SQLite's ON CONFLICT constraints natively.
    /// </summary>
    public async Task<bool> TryAcquireLeaseAsync(string jobId, string agentId, int durationSeconds)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();

        long now = JobLeaseRecord.CurrentEpoch;
        long expires = now + durationSeconds;

        string insertQueryQl = $@"
            INSERT INTO {JobLeaseRecord.TableNameQl} (JobId, AssignedAgentId, AcquiredAtEpoch, ExpiresAtEpoch)
            VALUES (@jobId, @agentId, @now, @expires)
            ON CONFLICT(JobId) DO UPDATE SET 
                AssignedAgentId = @agentId,
                AcquiredAtEpoch = @now,
                ExpiresAtEpoch = @expires
            WHERE ExpiresAtEpoch < @now;
        ";

        command.CommandText = insertQueryQl;

        // Execute returns 1 if inserted, 1 if updated, 0 if it failed the WHERE condition (someone else holds the actively valid lease)
        command.Parameters.Add(new SqliteParameter("@jobId", jobId));
        command.Parameters.Add(new SqliteParameter("@agentId", agentId));
        command.Parameters.Add(new SqliteParameter("@now", now));
        command.Parameters.Add(new SqliteParameter("@expires", expires));

        int rowsAffected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        return rowsAffected > 0;
    }

    /// <summary>
    /// Formally releases the semantic lease if the Agent matches securely naturally.
    /// </summary>
    public async Task ReleaseLeaseAsync(string jobId, string agentId)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();

        string deleteQueryQl = $@"
            DELETE FROM {JobLeaseRecord.TableNameQl}
            WHERE JobId = @jobId AND AssignedAgentId = @agentId;
        ";

        command.CommandText = deleteQueryQl;

        command.Parameters.Add(new SqliteParameter("@jobId", jobId));
        command.Parameters.Add(new SqliteParameter("@agentId", agentId));

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
