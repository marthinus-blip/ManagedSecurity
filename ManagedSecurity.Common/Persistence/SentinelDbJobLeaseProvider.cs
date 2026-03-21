using System.Threading.Tasks;
using Npgsql;
using ManagedSecurity.Common.Attributes;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// Provides atomic distributed queue locking mechanisms over native PostgreSQL using SKIP LOCKED mechanics natively.
/// Eliminates deadlocks cleanly, resolving high-throughput contention across Orchestrator task execution natively.
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
    /// Mathematically jumps blocked locks sequentially leveraging SKIP LOCKED to organically pull the next actionable Job.
    /// Employs `.RETURNING` natively dropping the roundtrip pipeline directly yielding the row organically.
    /// </summary>
    public async Task<JobLeaseRecord?> FetchNextJobAsync(string agentId, int durationSeconds)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        long now = JobLeaseRecord.CurrentEpoch;
        long expires = now + durationSeconds;

        string fetchQueryQl = $@"
            WITH UnlockedJob AS (
                SELECT Id 
                FROM {JobLeaseRecord.SchemaNameQl}.{JobLeaseRecord.TableNameQl}
                WHERE AssignedAgentId IS NULL OR ExpiresAtEpoch < @now
                ORDER BY Id ASC
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE {JobLeaseRecord.SchemaNameQl}.{JobLeaseRecord.TableNameQl} 
            SET AssignedAgentId = @agentId,
                AcquiredAtEpoch = @now,
                ExpiresAtEpoch = @expires
            FROM UnlockedJob
            WHERE {JobLeaseRecord.SchemaNameQl}.{JobLeaseRecord.TableNameQl}.Id = UnlockedJob.Id
            RETURNING {JobLeaseRecord.SchemaNameQl}.{JobLeaseRecord.TableNameQl}.Id, 
                      {JobLeaseRecord.SchemaNameQl}.{JobLeaseRecord.TableNameQl}.TenantId, 
                      {JobLeaseRecord.SchemaNameQl}.{JobLeaseRecord.TableNameQl}.JobType, 
                      {JobLeaseRecord.SchemaNameQl}.{JobLeaseRecord.TableNameQl}.Payload;
        ";

        command.CommandText = fetchQueryQl;
        command.Parameters.Add(new NpgsqlParameter("@agentId", agentId));
        command.Parameters.Add(new NpgsqlParameter("@now", now));
        command.Parameters.Add(new NpgsqlParameter("@expires", expires));

        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        
        // Physically assert if any row matched the constraint efficiently.
        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            return null; // Queue is systematically empty natively. Sleep conditionally.
        }

        return new JobLeaseRecord
        {
            Id = reader.GetInt64(0),
            TenantId = reader.GetInt64(1),
            JobType = reader.GetString(2),
            Payload = reader.GetString(3),
            AssignedAgentId = agentId,
            AcquiredAtEpoch = now,
            ExpiresAtEpoch = expires
        };
    }

    /// <summary>
    /// Formally releases the semantic lease logically yielding it back into the queue organically.
    /// Alternatively cleanly deletes it if the task succeeded conclusively.
    /// </summary>
    public async Task ReleaseLeaseAsync(long jobId, string agentId)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // Physically deleting the row constitutes a "Success". 
        // We evaluate deletion formally over the JobId AND strictly the specific AssignedAgentId natively preventing rogue snipes.
        string deleteQueryQl = $@"
            DELETE FROM {JobLeaseRecord.SchemaNameQl}.{JobLeaseRecord.TableNameQl}
            WHERE Id = @jobId AND AssignedAgentId = @agentId;
        ";

        command.CommandText = deleteQueryQl;

        command.Parameters.Add(new NpgsqlParameter("@jobId", jobId));
        command.Parameters.Add(new NpgsqlParameter("@agentId", agentId));

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
