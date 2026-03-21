using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using ManagedSecurity.Common.Attributes;

namespace ManagedSecurity.Common.Persistence;

[AllowMagicValues]
public class SentinelDbTenantProvider : ITenantProvider
{
    private readonly ISentinelDbConnectionFactory _connectionFactory;

    public SentinelDbTenantProvider(ISentinelDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<IReadOnlyList<TenantRecord>> GetAuthorizedTenantsForUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        string tTable = _connectionFactory.Dialect.TranslateTableNamespace(TenantRecord.SchemaNameQl, TenantRecord.TableNameQl);
        string tuaTable = _connectionFactory.Dialect.TranslateTableNamespace(TenantUserAccessRecord.SchemaNameQl, TenantUserAccessRecord.TableNameQl);

        string query = $@"
            SELECT 
                t.TenantId, t.OrganizationName, t.IsDeleted, 
                t.CreatedAtEpoch, t.UpdatedAtEpoch, t.UpdatedByUserId
            FROM {tTable} t
            INNER JOIN {tuaTable} tua 
                ON t.TenantId = tua.TenantId
            WHERE tua.UserId = @UserId AND t.IsDeleted = false;
        ";

        var results = new List<TenantRecord>();
        
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = query;

        AddParameter(command, "@UserId", DbType.Int64, userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new TenantRecord
            {
                TenantId = reader.GetInt64(0),
                OrganizationName = reader.GetString(1),
                IsDeleted = reader.GetBoolean(2),
                CreatedAtEpoch = reader.GetInt64(3),
                UpdatedAtEpoch = reader.GetInt64(4),
                UpdatedByUserId = reader.IsDBNull(5) ? 0 : reader.GetInt64(5)
            });
        }

        return results;
    }

    public async Task<long> CreateTenantAsync(TenantRecord record, CancellationToken cancellationToken = default)
    {
        string tTable = _connectionFactory.Dialect.TranslateTableNamespace(TenantRecord.SchemaNameQl, TenantRecord.TableNameQl);

        string query = $@"
            INSERT INTO {tTable} 
            (OrganizationName, IsDeleted, CreatedAtEpoch, UpdatedAtEpoch, UpdatedByUserId)
            VALUES (@OrganizationName, @IsDeleted, @CreatedAtEpoch, @UpdatedAtEpoch, @UpdatedByUserId)
            RETURNING TenantId;
        ";

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = query;

        AddParameter(command, "@OrganizationName", DbType.String, record.OrganizationName);
        AddParameter(command, "@IsDeleted", DbType.Boolean, record.IsDeleted);
        AddParameter(command, "@CreatedAtEpoch", DbType.Int64, record.CreatedAtEpoch);
        AddParameter(command, "@UpdatedAtEpoch", DbType.Int64, record.UpdatedAtEpoch);
        AddParameter(command, "@UpdatedByUserId", DbType.Int64, record.UpdatedByUserId);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    public async Task GrantTenantAccessAsync(TenantUserAccessRecord record, CancellationToken cancellationToken = default)
    {
        string tuaTable = _connectionFactory.Dialect.TranslateTableNamespace(TenantUserAccessRecord.SchemaNameQl, TenantUserAccessRecord.TableNameQl);

        string query = $@"
            INSERT INTO {tuaTable} 
            (TenantId, UserId, RoleLevel, GrantedAtEpoch)
            VALUES (@TenantId, @UserId, @RoleLevel, @GrantedAtEpoch);
        ";

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = query;

        AddParameter(command, "@TenantId", DbType.Int64, record.TenantId);
        AddParameter(command, "@UserId", DbType.Int64, record.UserId);
        AddParameter(command, "@RoleLevel", DbType.Int16, record.RoleLevel);
        AddParameter(command, "@GrantedAtEpoch", DbType.Int64, record.GrantedAtEpoch);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
