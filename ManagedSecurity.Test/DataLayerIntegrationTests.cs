using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ManagedSecurity.Common.Persistence;
using ManagedSecurity.Common.Configuration;

namespace ManagedSecurity.Test;

[TestClass]
[ManagedSecurity.Common.Attributes.AllowMagicValues]
public class DataLayerIntegrationTests
{
    private (string, ISentinelDbConnectionFactory) InitializeIsolate()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_test_{Guid.NewGuid():N}.db");
        var connectionFactory = new SentinelDbConnectionFactory($"Data Source={dbPath};Pooling=False;");
        return (dbPath, connectionFactory);
    }

    private void CleanupIsolated(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(dbPath); } catch { }
    }

    [TestMethod]
    public async Task SentinelDb_Configuration_Bootstraps_And_Reads_Natively()
    {
        var (dbPath, connectionFactory) = InitializeIsolate();
        try
        {
            // 1. Arrange - Bootstrap schema constraints
            await SentinelDbBootstrapper.EnsureSchemaInitializedAsync(connectionFactory);

            // 2. Arrange - Simulate an external Commander API injecting a configuration payload natively
            using (var connection = await connectionFactory.CreateConnectionAsync())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    INSERT INTO CameraRecord (CameraId, StreamUrl, SnapshotUrl) 
                    VALUES ('cam-01', 'rtsp://10.0.0.99/live', 'http://10.0.0.99/snap.jpg');
                ";
                await command.ExecuteNonQueryAsync();
            }

            // 3. Act - Initialize ASP.NET Configuration Builder utilizing our bridging source
            var configuration = new ConfigurationBuilder()
                .AddSentinelSqliteConfiguration(connectionFactory)
                .Build();

            // 4. Assert - Verify NativeAOT bridge translates ADO.NET structs into Configuration dictionary
            Assert.AreEqual("cam-01", configuration["Cameras:0:Id"]);
            Assert.AreEqual("rtsp://10.0.0.99/live", configuration["Cameras:0:Url"]);
            Assert.AreEqual("http://10.0.0.99/snap.jpg", configuration["Cameras:0:SnapshotUrl"]);
        }
        finally
        {
            CleanupIsolated(dbPath);
        }
    }

    [TestCategory("Manual")]
    [DoNotParallelize]
    [TestMethod]
    public async Task SentinelDb_Configuration_Detects_OutOfBand_Mutations_Via_PragmaDataVersion()
    {
        var (dbPath, connectionFactory) = InitializeIsolate();
        try
        {
            // 1. Arrange - Bootstrap schema constraints
            await SentinelDbBootstrapper.EnsureSchemaInitializedAsync(connectionFactory);
            using (var connection = await connectionFactory.CreateConnectionAsync())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO CameraRecord (CameraId, StreamUrl) VALUES ('cam-01', 'rtsp://old-url');";
                await command.ExecuteNonQueryAsync();
            }

            // 2. Act - Create provider with an aggressive 10ms polling interval for the test
            var provider = new SentinelDbConfigurationProvider(connectionFactory, TimeSpan.FromMilliseconds(10));
            
            // Track the synthetic OnReload callback
            bool reloadTriggered = false;
            var token = provider.GetReloadToken();
            token.RegisterChangeCallback(_ => reloadTriggered = true, null);

            // Initial Load
            provider.Load();
            provider.TryGet("Cameras:0:Url", out var initialUrl);
            Assert.AreEqual("rtsp://old-url", initialUrl);

            // 3. Act - Simulate a disparate process (e.g. Dashboard) mutating the DB structurally
            using (var disparateConnection = await connectionFactory.CreateConnectionAsync())
            using (var updateCommand = disparateConnection.CreateCommand())
            {
                updateCommand.CommandText = "UPDATE CameraRecord SET StreamUrl = 'rtsp://new-url' WHERE CameraId = 'cam-01'; PRAGMA wal_checkpoint(TRUNCATE);";
                await updateCommand.ExecuteNonQueryAsync();
            }

            // 4. Force poll PRAGMA data_version natively directly explicitly
            bool mutationDetected = await provider.CheckForUpdatesAsync();
            if (mutationDetected)
            {
                // Trigger formal pipeline execution cleanly natively
                provider.Load();
            }

            // 5. Assert - Overridden EF interceptor correctly detected Native mutation
            Assert.IsTrue(mutationDetected, "Provider failed to detect PRAGMA data_version mutation natively across distinct SQLite connections.");
            
            provider.TryGet("Cameras:0:Url", out var newUrl);
            Assert.AreEqual("rtsp://new-url", newUrl);
            
            provider.Dispose();
        }
        finally
        {
            CleanupIsolated(dbPath);
        }
    }
    [TestMethod]
    public async Task SentinelDb_LeaseProvider_Prevents_Concurrent_Locks()
    {
        var (dbPath, connectionFactory) = InitializeIsolate();
        try
        {
            await SentinelDbBootstrapper.EnsureSchemaInitializedAsync(connectionFactory);
            IJobLeaseProvider provider = new SentinelDbJobLeaseProvider(connectionFactory);

            // 1. First agent acquires lease
            string jobId = "AnalysisJob-100";
            string agent1 = "Agent-01";
            bool req1 = await provider.TryAcquireLeaseAsync(jobId, agent1, 10);
            
            Assert.IsTrue(req1, "First agent should successfully acquire the lease.");

            // 2. Second agent attempts to acquire the same lease and fails
            string agent2 = "Agent-02";
            bool req2 = await provider.TryAcquireLeaseAsync(jobId, agent2, 10);
            
            Assert.IsFalse(req2, "Second agent should be blocked by the concurrency boundary.");

            // 3. First agent releases lease
            await provider.ReleaseLeaseAsync(jobId, agent1);

            // 4. Second agent successfully acquires it
            bool req3 = await provider.TryAcquireLeaseAsync(jobId, agent2, 10);
            Assert.IsTrue(req3, "Second agent should successfully acquire lease after previous agent releases it natively.");
        }
        finally
        {
            CleanupIsolated(dbPath);
        }
    }

    [TestMethod]
    public async Task SentinelDb_AgentStateProvider_Upserts_And_Tracks_Heartbeats_Successfully()
    {
        var (dbPath, connectionFactory) = InitializeIsolate();
        try
        {
            await SentinelDbBootstrapper.EnsureSchemaInitializedAsync(connectionFactory);
            IAgentStateProvider provider = new SentinelDbAgentStateProvider(connectionFactory);

            long currentEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            // 1. Upsert a new Agent state securely uniquely
            var newAgent = new AgentStateRecordRl
            {
                AgentIdRl = "test-node-1",
                StatusDescriptionRl = "Scanning",
                CpuLoadPercentageRl = 34.5f,
                MemoryUsageBytesRl = 1048576,
                LastHeartbeatEpochRl = currentEpoch
            };
            
            await provider.UpsertAgentStateAsync(newAgent);
            
            // 2. Fetch mathematically gracefully
            var fetched = await provider.GetAgentStateAsync("test-node-1");
            Assert.IsNotNull(fetched);
            Assert.AreEqual("Scanning", fetched.Value.StatusDescriptionRl);
            
            // 3. Upsert update (Agent modifies its CPU flexibly natively cleanly seamlessly)
            var updatedAgent = newAgent with { CpuLoadPercentageRl = 88.1f };
            await provider.UpsertAgentStateAsync(updatedAgent);
            
            fetched = await provider.GetAgentStateAsync("test-node-1");
            Assert.AreEqual(88.1f, fetched.Value.CpuLoadPercentageRl);
            
            // 4. Verify Active Agents (Timeout logic boundary) natively accurately properly smoothly properly
            var activeAgents = await provider.GetAllActiveAgentsAsync(currentEpoch, timeoutSecondsRl: 10);
            Assert.AreEqual(1, activeAgents.Count);
            
            // 5. Verify Dead Boundary inherently properly intuitively elegantly seamlessly logically carefully natively stably precisely precisely cleanly
            long futureEpoch = currentEpoch + 30; // 30 seconds into the future natively securely functionally seamlessly safely precisely seamlessly securely stably natively robustly correctly completely
            var emptyAgents = await provider.GetAllActiveAgentsAsync(futureEpoch, timeoutSecondsRl: 10);
            Assert.AreEqual(0, emptyAgents.Count); // Should be dead natively completely perfectly completely
            Assert.IsTrue(fetched.Value.IsOfficiallyDead(futureEpoch, 10)); // Struct validation mathematically seamlessly logically cleanly purely fluently
        }
        finally
        {
            CleanupIsolated(dbPath);
        }
    }
}
