using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using ManagedSecurity.Common.Attributes;

namespace ManagedSecurity.Common.Persistence;

[AllowMagicValues]
public class SentinelPostgresUserProvider : IUserProvider
{
    // Following .standards.md: Zero Magic Values & Explicit Schemas
    public const string SchemaNameQl = "auth";
    public const string TableNameQl = "Users";

    private readonly ISentinelDbConnectionFactory _connectionFactory;

    public SentinelPostgresUserProvider(ISentinelDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<UserRecord?> GetUserByEmailAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        // Note: PostgreSQL Partial Index (`WHERE IsDeleted = false`) enforces uniqueness natively.
        // We implicitly query active identities only.
        const string query = $@"
            SELECT 
                UserId, EmailAddress, PasswordHash, SecurityStamp, IsDeleted, 
                CreatedAtEpoch, UpdatedAtEpoch, UpdatedByUserId
            FROM {SchemaNameQl}.{TableNameQl}
            WHERE EmailAddress = @EmailAddress AND IsDeleted = false;
        ";

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = query;

        AddParameter(command, "@EmailAddress", DbType.String, emailAddress);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new UserRecord
            {
                UserId = reader.GetInt64(0),
                EmailAddress = reader.GetString(1),
                PasswordHash = reader.GetString(2),
                SecurityStamp = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                IsDeleted = reader.GetBoolean(4),
                CreatedAtEpoch = reader.GetInt64(5),
                UpdatedAtEpoch = reader.GetInt64(6),
                UpdatedByUserId = reader.IsDBNull(7) ? 0 : reader.GetInt64(7)
            };
        }

        return null; // The Identity does not exist (Yields cleanly for dummy-hash execution)
    }

    public async Task<long> CreateUserAsync(UserRecord record, CancellationToken cancellationToken = default)
    {
        const string query = $@"
            INSERT INTO {SchemaNameQl}.{TableNameQl} 
            (EmailAddress, PasswordHash, SecurityStamp, IsDeleted, CreatedAtEpoch, UpdatedAtEpoch, UpdatedByUserId)
            VALUES (@EmailAddress, @PasswordHash, @SecurityStamp, @IsDeleted, @CreatedAtEpoch, @UpdatedAtEpoch, @UpdatedByUserId)
            RETURNING UserId;
        ";

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = query;

        AddParameter(command, "@EmailAddress", DbType.String, record.EmailAddress);
        AddParameter(command, "@PasswordHash", DbType.String, record.PasswordHash);
        AddParameter(command, "@SecurityStamp", DbType.String, record.SecurityStamp);
        AddParameter(command, "@IsDeleted", DbType.Boolean, record.IsDeleted);
        AddParameter(command, "@CreatedAtEpoch", DbType.Int64, record.CreatedAtEpoch);
        AddParameter(command, "@UpdatedAtEpoch", DbType.Int64, record.UpdatedAtEpoch);
        AddParameter(command, "@UpdatedByUserId", DbType.Int64, record.UpdatedByUserId);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result);
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
