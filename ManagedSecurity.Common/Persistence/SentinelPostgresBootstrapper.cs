using System.Threading.Tasks;
using Npgsql;
using ManagedSecurity.Common.Attributes;

namespace ManagedSecurity.Common.Persistence;

/// <summary>
/// Statically executes native ADO.NET DDL boundaries to format PostgreSQL Row-Level Security architecture.
/// Completely negates the demand for Entity Framework Core runtime migrations while establishing physical Zero-Trust bindings [INSC-OPT].
/// </summary>
public static class SentinelPostgresBootstrapper
{
    [AllowMagicValues]
    public static async Task EnsureSchemaInitializedAsync(ISentinelDbConnectionFactory connectionFactory)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = $@"
            CREATE SCHEMA IF NOT EXISTS {UserRecord.SchemaNameQl};

            CREATE TABLE IF NOT EXISTS {UserRecord.SchemaNameQl}.{UserRecord.TableNameQl} (
                UserId BIGSERIAL PRIMARY KEY,
                GlobalIdentity TEXT NOT NULL UNIQUE,
                Argon2IdHash TEXT NOT NULL,
                IsDeleted BOOLEAN NOT NULL DEFAULT FALSE,
                CreatedAtEpoch BIGINT NOT NULL,
                UpdatedAtEpoch BIGINT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS {TenantRecord.SchemaNameQl}.{TenantRecord.TableNameQl} (
                TenantId BIGSERIAL PRIMARY KEY,
                OrganizationName TEXT NOT NULL,
                IsDeleted BOOLEAN NOT NULL DEFAULT FALSE,
                CreatedAtEpoch BIGINT NOT NULL,
                UpdatedAtEpoch BIGINT NOT NULL,
                UpdatedByUserId BIGINT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS {TenantUserAccessRecord.SchemaNameQl}.{TenantUserAccessRecord.TableNameQl} (
                TenantId BIGINT NOT NULL,
                UserId BIGINT NOT NULL,
                RoleLevel SMALLINT NOT NULL,
                GrantedAtEpoch BIGINT NOT NULL,
                PRIMARY KEY (TenantId, UserId)
            );

            -- RLS Protected Tables (Cross-Tenant Execution Boundaries)

            CREATE TABLE IF NOT EXISTS {CameraRecord.SchemaNameQl}.{CameraRecord.TableNameQl} (
                CameraId TEXT,
                TenantId BIGINT NOT NULL,
                DisplayName TEXT,
                StreamUrl TEXT,
                SnapshotUrl TEXT,
                Vendor TEXT,
                Model TEXT,
                IpAddress TEXT,
                Port INTEGER,
                MachineVisionRoute INTEGER,
                EncryptedVaultCredentials BYTEA,
                SecurityNonce BYTEA,
                IsOrchestrationLeased INTEGER,
                PRIMARY KEY (CameraId, TenantId)
            );

            CREATE TABLE IF NOT EXISTS {JobLeaseRecord.SchemaNameQl}.{JobLeaseRecord.TableNameQl} (
                Id BIGSERIAL PRIMARY KEY,
                TenantId BIGINT NOT NULL,
                JobType TEXT NOT NULL,
                Payload TEXT NOT NULL,
                AssignedAgentId TEXT,
                AcquiredAtEpoch BIGINT NOT NULL DEFAULT 0,
                ExpiresAtEpoch BIGINT NOT NULL DEFAULT 0,
                RetryCount INT NOT NULL DEFAULT 0,
                MaxRetries INT NOT NULL DEFAULT 3,
                StatePayload TEXT,
                LastError TEXT
            );

            CREATE TABLE IF NOT EXISTS {AgentStateRecordRl.SchemaNameQl}.{AgentStateRecordRl.TableNameQl} (
                {AgentStateRecordRl.AgentIdQl} TEXT,
                TenantId BIGINT NOT NULL,
                {AgentStateRecordRl.StatusDescriptionQl} TEXT NOT NULL,
                {AgentStateRecordRl.CpuLoadPercentageQl} REAL NOT NULL,
                {AgentStateRecordRl.MemoryUsageBytesQl} INTEGER NOT NULL,
                {AgentStateRecordRl.LastHeartbeatEpochQl} BIGINT NOT NULL,
                PRIMARY KEY ({AgentStateRecordRl.AgentIdQl}, TenantId)
            );

            -- Enforce PostgreSQL Row-Level Security explicitly locking all underlying query evaluations structurally natively [INSC-OPT]
            
            ALTER TABLE {CameraRecord.SchemaNameQl}.{CameraRecord.TableNameQl} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {CameraRecord.SchemaNameQl}.{CameraRecord.TableNameQl} FORCE ROW LEVEL SECURITY;

            ALTER TABLE {JobLeaseRecord.SchemaNameQl}.{JobLeaseRecord.TableNameQl} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {JobLeaseRecord.SchemaNameQl}.{JobLeaseRecord.TableNameQl} FORCE ROW LEVEL SECURITY;

            ALTER TABLE {AgentStateRecordRl.SchemaNameQl}.{AgentStateRecordRl.TableNameQl} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {AgentStateRecordRl.SchemaNameQl}.{AgentStateRecordRl.TableNameQl} FORCE ROW LEVEL SECURITY;

            -- Establish Zero-Trust App Parameter Binding (Using the exact session scope context injected by SentinelPostgresConnectionFactory)
            -- IF NOT EXISTS handles graceful DDL re-execution bounds accurately cleanly seamlessly [ESC-OPT].

            DO $$ 
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'tenant_isolation_camera_policy') THEN
                    CREATE POLICY tenant_isolation_camera_policy ON {CameraRecord.SchemaNameQl}.{CameraRecord.TableNameQl}  
                        USING (TenantId = current_setting('app.current_tenant_id', true)::BIGINT);
                END IF;

                IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'tenant_isolation_job_policy') THEN
                    CREATE POLICY tenant_isolation_job_policy ON {JobLeaseRecord.SchemaNameQl}.{JobLeaseRecord.TableNameQl} 
                        USING (TenantId = current_setting('app.current_tenant_id', true)::BIGINT);
                END IF;

                IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE policyname = 'tenant_isolation_agent_policy') THEN
                    CREATE POLICY tenant_isolation_agent_policy ON {AgentStateRecordRl.SchemaNameQl}.{AgentStateRecordRl.TableNameQl} 
                        USING (TenantId = current_setting('app.current_tenant_id', true)::BIGINT);
                END IF;
            END $$;
        ";

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
