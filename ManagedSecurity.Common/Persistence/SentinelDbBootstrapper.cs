using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace ManagedSecurity.Common.Persistence;

public static class SentinelDbBootstrapper
{
    /// <summary>
    /// Statically executes native ADO.NET DDL boundaries to format table architecture 
    /// if missing. Completely negates the demand for Entity Framework Core runtime migrations
    /// and ensures optimal NativeAOT database alignment on first boot.
    /// </summary>
    [ManagedSecurity.Common.Attributes.AllowMagicValues]
    public static async Task EnsureSchemaInitializedAsync(ISentinelDbConnectionFactory connectionFactory)
    {
        using var connection = await connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();

        command.CommandText = $@"
            CREATE TABLE IF NOT EXISTS {UserRecord.SchemaNameQl}_{UserRecord.TableNameQl} (
                UserId INTEGER PRIMARY KEY AUTOINCREMENT,
                EmailAddress TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                SecurityStamp TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                CreatedAtEpoch INTEGER NOT NULL,
                UpdatedAtEpoch INTEGER NOT NULL,
                UpdatedByUserId INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS {TenantRecord.SchemaNameQl}_{TenantRecord.TableNameQl} (
                TenantId INTEGER PRIMARY KEY AUTOINCREMENT,
                OrganizationName TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                CreatedAtEpoch INTEGER NOT NULL,
                UpdatedAtEpoch INTEGER NOT NULL,
                UpdatedByUserId INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS {TenantUserAccessRecord.SchemaNameQl}_{TenantUserAccessRecord.TableNameQl} (
                TenantId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                RoleLevel INTEGER NOT NULL,
                CapabilitiesJson TEXT NOT NULL DEFAULT '[]',
                GrantedAtEpoch INTEGER NOT NULL,
                PRIMARY KEY (TenantId, UserId)
            );

            CREATE TABLE IF NOT EXISTS {CameraRecord.SchemaNameQl}_{CameraRecord.TableNameQl} (
                CameraId TEXT,
                TenantId INTEGER NOT NULL,
                DisplayName TEXT,
                StreamUrl TEXT,
                SnapshotUrl TEXT,
                Vendor TEXT,
                Model TEXT,
                IpAddress TEXT,
                Port INTEGER,
                MachineVisionRoute INTEGER,
                EncryptedVaultCredentials BLOB,
                SecurityNonce BLOB,
                IsOrchestrationLeased INTEGER,
                PRIMARY KEY (CameraId, TenantId)
            );

            CREATE TABLE IF NOT EXISTS {JobLeaseRecord.SchemaNameQl}_{JobLeaseRecord.TableNameQl} (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TenantId INTEGER NOT NULL,
                JobType TEXT NOT NULL,
                Payload TEXT NOT NULL,
                AssignedAgentId TEXT,
                AcquiredAtEpoch INTEGER NOT NULL DEFAULT 0,
                ExpiresAtEpoch INTEGER NOT NULL DEFAULT 0,
                RetryCount INTEGER NOT NULL DEFAULT 0,
                MaxRetries INTEGER NOT NULL DEFAULT 3,
                StatePayload TEXT,
                LastError TEXT
            );

            CREATE TABLE IF NOT EXISTS {AgentStateRecordRl.SchemaNameQl}_{AgentStateRecordRl.TableNameQl} (
                {AgentStateRecordRl.AgentIdQl} TEXT,
                TenantId INTEGER NOT NULL,
                {AgentStateRecordRl.StatusDescriptionQl} TEXT NOT NULL,
                {AgentStateRecordRl.CpuLoadPercentageQl} REAL NOT NULL,
                {AgentStateRecordRl.MemoryUsageBytesQl} INTEGER NOT NULL,
                {AgentStateRecordRl.LastHeartbeatEpochQl} INTEGER NOT NULL,
                PRIMARY KEY ({AgentStateRecordRl.AgentIdQl}, TenantId)
            );
        ";

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
