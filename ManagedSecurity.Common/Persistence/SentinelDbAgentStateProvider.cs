using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ManagedSecurity.Common.Attributes;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// A native concrete implementation inherently tracking Ground Truth SQLite schemas dynamically efficiently efficiently compactly safely cleanly accurately.
/// </summary>
[AllowMagicValues]
public class SentinelDbAgentStateProvider : IAgentStateProvider
{
    private readonly ISentinelDbConnectionFactory _connectionFactory;

    public SentinelDbAgentStateProvider(ISentinelDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task UpsertAgentStateAsync(AgentStateRecordRl stateRecord)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();

        string upsertQueryQl = $@"
            INSERT INTO {AgentStateRecordRl.TableNameQl} (
                {AgentStateRecordRl.AgentIdQl}, 
                {AgentStateRecordRl.StatusDescriptionQl}, 
                {AgentStateRecordRl.CpuLoadPercentageQl}, 
                {AgentStateRecordRl.MemoryUsageBytesQl}, 
                {AgentStateRecordRl.LastHeartbeatEpochQl}
            )
            VALUES (@id, @status, @cpu, @mem, @epoch)
            ON CONFLICT({AgentStateRecordRl.AgentIdQl}) DO UPDATE SET 
                {AgentStateRecordRl.StatusDescriptionQl} = @status,
                {AgentStateRecordRl.CpuLoadPercentageQl} = @cpu,
                {AgentStateRecordRl.MemoryUsageBytesQl} = @mem,
                {AgentStateRecordRl.LastHeartbeatEpochQl} = @epoch;
        ";

        command.CommandText = upsertQueryQl;

        command.Parameters.Add(new SqliteParameter("@id", stateRecord.AgentIdRl));
        command.Parameters.Add(new SqliteParameter("@status", stateRecord.StatusDescriptionRl));
        command.Parameters.Add(new SqliteParameter("@cpu", stateRecord.CpuLoadPercentageRl));
        command.Parameters.Add(new SqliteParameter("@mem", stateRecord.MemoryUsageBytesRl));
        command.Parameters.Add(new SqliteParameter("@epoch", stateRecord.LastHeartbeatEpochRl));

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<AgentStateRecordRl?> GetAgentStateAsync(string agentIdRl)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();

        string selectQueryQl = $@"
            SELECT 
                {AgentStateRecordRl.StatusDescriptionQl}, 
                {AgentStateRecordRl.CpuLoadPercentageQl}, 
                {AgentStateRecordRl.MemoryUsageBytesQl}, 
                {AgentStateRecordRl.LastHeartbeatEpochQl}
            FROM {AgentStateRecordRl.TableNameQl}
            WHERE {AgentStateRecordRl.AgentIdQl} = @id;
        ";

        command.CommandText = selectQueryQl;
        command.Parameters.Add(new SqliteParameter("@id", agentIdRl));

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            return new AgentStateRecordRl
            {
                AgentIdRl = agentIdRl,
                StatusDescriptionRl = reader.GetString(0),
                CpuLoadPercentageRl = reader.GetFloat(1),
                MemoryUsageBytesRl = reader.GetInt64(2),
                LastHeartbeatEpochRl = reader.GetInt64(3)
            };
        }

        return null;
    }

    public async Task<List<AgentStateRecordRl>> GetAllActiveAgentsAsync(long currentEpochRl, int timeoutSecondsRl)
    {
        var agents = new List<AgentStateRecordRl>();
        long cutoffEpochRl = currentEpochRl - timeoutSecondsRl;

        using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();

        string selectActiveQl = $@"
            SELECT 
                {AgentStateRecordRl.AgentIdQl},
                {AgentStateRecordRl.StatusDescriptionQl}, 
                {AgentStateRecordRl.CpuLoadPercentageQl}, 
                {AgentStateRecordRl.MemoryUsageBytesQl}, 
                {AgentStateRecordRl.LastHeartbeatEpochQl}
            FROM {AgentStateRecordRl.TableNameQl}
            WHERE {AgentStateRecordRl.LastHeartbeatEpochQl} >= @cutoff;
        ";

        command.CommandText = selectActiveQl;
        command.Parameters.Add(new SqliteParameter("@cutoff", cutoffEpochRl));

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            agents.Add(new AgentStateRecordRl
            {
                AgentIdRl = reader.GetString(0),
                StatusDescriptionRl = reader.GetString(1),
                CpuLoadPercentageRl = reader.GetFloat(2),
                MemoryUsageBytesRl = reader.GetInt64(3),
                LastHeartbeatEpochRl = reader.GetInt64(4)
            });
        }

        return agents;
    }

    public async Task RemoveAgentStateAsync(string agentIdRl)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();

        string deleteQueryQl = $@"
            DELETE FROM {AgentStateRecordRl.TableNameQl}
            WHERE {AgentStateRecordRl.AgentIdQl} = @id;
        ";

        command.CommandText = deleteQueryQl;
        command.Parameters.Add(new SqliteParameter("@id", agentIdRl));

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
