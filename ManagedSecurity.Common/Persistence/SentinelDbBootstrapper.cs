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
            CREATE TABLE IF NOT EXISTS {nameof(CameraRecord)} (
                CameraId TEXT PRIMARY KEY,
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
                IsOrchestrationLeased INTEGER
            );

            CREATE TABLE IF NOT EXISTS {JobLeaseRecord.TableNameQl} (
                JobId TEXT PRIMARY KEY,
                AssignedAgentId TEXT NOT NULL,
                AcquiredAtEpoch INTEGER NOT NULL,
                ExpiresAtEpoch INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS {AgentStateRecordRl.TableNameQl} (
                {AgentStateRecordRl.AgentIdQl} TEXT PRIMARY KEY,
                {AgentStateRecordRl.StatusDescriptionQl} TEXT NOT NULL,
                {AgentStateRecordRl.CpuLoadPercentageQl} REAL NOT NULL,
                {AgentStateRecordRl.MemoryUsageBytesQl} INTEGER NOT NULL,
                {AgentStateRecordRl.LastHeartbeatEpochQl} INTEGER NOT NULL
            );
        ";

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
