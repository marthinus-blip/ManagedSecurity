using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using ManagedSecurity.Common.Attributes;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// A native concrete implementation inherently tracking Ground Truth schemas dynamically explicitly bridging agnostic SQLite and PostgreSQL explicitly cleanly [ESC-OPT].
/// </summary>
[AllowMagicValues]
public class SentinelDbAgentStateProvider : IAgentStateProvider
{
    private readonly ISentinelDbConnectionFactory _connectionFactory;
    private readonly ITenantContextAccessor _tenantAccessor;

    public SentinelDbAgentStateProvider(ISentinelDbConnectionFactory connectionFactory, ITenantContextAccessor tenantAccessor)
    {
        _connectionFactory = connectionFactory;
        _tenantAccessor = tenantAccessor;
    }

    public async Task UpsertAgentStateAsync(AgentStateRecordRl stateRecord)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();

        string tableTarget = _connectionFactory.Dialect.TranslateTableNamespace(AgentStateRecordRl.SchemaNameQl, AgentStateRecordRl.TableNameQl);

        string upsertQueryQl = $@"
            INSERT INTO {tableTarget} (
                {AgentStateRecordRl.AgentIdQl}, 
                TenantId,
                {AgentStateRecordRl.StatusDescriptionQl}, 
                {AgentStateRecordRl.CpuLoadPercentageQl}, 
                {AgentStateRecordRl.MemoryUsageBytesQl}, 
                {AgentStateRecordRl.LastHeartbeatEpochQl}
            )
            VALUES (@id, @tenantId, @status, @cpu, @mem, @epoch)
            ON CONFLICT({AgentStateRecordRl.AgentIdQl}, TenantId) DO UPDATE SET 
                {AgentStateRecordRl.StatusDescriptionQl} = @status,
                {AgentStateRecordRl.CpuLoadPercentageQl} = @cpu,
                {AgentStateRecordRl.MemoryUsageBytesQl} = @mem,
                {AgentStateRecordRl.LastHeartbeatEpochQl} = @epoch;
        ";

        command.CommandText = upsertQueryQl;

        AddParameter(command, "@id", stateRecord.AgentIdRl);
        AddParameter(command, "@tenantId", _tenantAccessor.GetActiveTenantId());
        AddParameter(command, "@status", stateRecord.StatusDescriptionRl);
        AddParameter(command, "@cpu", stateRecord.CpuLoadPercentageRl);
        AddParameter(command, "@mem", stateRecord.MemoryUsageBytesRl);
        AddParameter(command, "@epoch", stateRecord.LastHeartbeatEpochRl);

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<AgentStateRecordRl?> GetAgentStateAsync(string agentIdRl)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();

        string tableTarget = _connectionFactory.Dialect.TranslateTableNamespace(AgentStateRecordRl.SchemaNameQl, AgentStateRecordRl.TableNameQl);

        string selectQueryQl = $@"
            SELECT 
                {AgentStateRecordRl.StatusDescriptionQl}, 
                {AgentStateRecordRl.CpuLoadPercentageQl}, 
                {AgentStateRecordRl.MemoryUsageBytesQl}, 
                {AgentStateRecordRl.LastHeartbeatEpochQl}
            FROM {tableTarget}
            WHERE {AgentStateRecordRl.AgentIdQl} = @id AND TenantId = @tenantId;
        ";

        command.CommandText = selectQueryQl;
        AddParameter(command, "@id", agentIdRl);
        AddParameter(command, "@tenantId", _tenantAccessor.GetActiveTenantId());

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

        string tableTarget = _connectionFactory.Dialect.TranslateTableNamespace(AgentStateRecordRl.SchemaNameQl, AgentStateRecordRl.TableNameQl);

        string selectActiveQl = $@"
            SELECT 
                {AgentStateRecordRl.AgentIdQl},
                {AgentStateRecordRl.StatusDescriptionQl}, 
                {AgentStateRecordRl.CpuLoadPercentageQl}, 
                {AgentStateRecordRl.MemoryUsageBytesQl}, 
                {AgentStateRecordRl.LastHeartbeatEpochQl}
            FROM {tableTarget}
            WHERE {AgentStateRecordRl.LastHeartbeatEpochQl} >= @cutoff AND TenantId = @tenantId;
        ";

        command.CommandText = selectActiveQl;
        AddParameter(command, "@cutoff", cutoffEpochRl);
        AddParameter(command, "@tenantId", _tenantAccessor.GetActiveTenantId());

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

        string tableTarget = _connectionFactory.Dialect.TranslateTableNamespace(AgentStateRecordRl.SchemaNameQl, AgentStateRecordRl.TableNameQl);

        string deleteQueryQl = $@"
            DELETE FROM {tableTarget}
            WHERE {AgentStateRecordRl.AgentIdQl} = @id AND TenantId = @tenantId;
        ";

        command.CommandText = deleteQueryQl;
        AddParameter(command, "@id", agentIdRl);
        AddParameter(command, "@tenantId", _tenantAccessor.GetActiveTenantId());

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? System.DBNull.Value;
        command.Parameters.Add(p);
    }
}
