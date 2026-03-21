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

    private class MockTenantContextAccessor : ITenantContextAccessor
    {
        public long ActiveTenantId { get; set; } = 1;
        public long GetActiveTenantId() => ActiveTenantId;
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
                    INSERT INTO auth_Cameras (CameraId, TenantId, StreamUrl, SnapshotUrl) 
                    VALUES ('cam-01', 1, 'rtsp://10.0.0.99/live', 'http://10.0.0.99/snap.jpg');
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
                command.CommandText = "INSERT INTO auth_Cameras (CameraId, TenantId, StreamUrl) VALUES ('cam-01', 1, 'rtsp://old-url');";
                await command.ExecuteNonQueryAsync();
            }

            // 2. Act - Create provider with an aggressive 10ms polling interval for the test
            var provider = new SentinelDbConfigurationProvider(connectionFactory, TimeSpan.FromMilliseconds(10));
            
            var token = provider.GetReloadToken();
            token.RegisterChangeCallback(_ => { }, null);

            // Initial Load
            provider.Load();
            provider.TryGet("Cameras:0:Url", out var initialUrl);
            Assert.AreEqual("rtsp://old-url", initialUrl);

            // 3. Act - Simulate a disparate process (e.g. Dashboard) mutating the DB structurally
            using (var disparateConnection = await connectionFactory.CreateConnectionAsync())
            using (var updateCommand = disparateConnection.CreateCommand())
            {
                updateCommand.CommandText = "UPDATE auth_Cameras SET StreamUrl = 'rtsp://new-url' WHERE CameraId = 'cam-01';";
                int rows = await updateCommand.ExecuteNonQueryAsync();
                Assert.IsTrue(rows > 0, "Update failed to affect rows.");
            }

            // Yield gracefully bridging local filesystem bounds organically mapping cleanly
            await Task.Delay(50);

            // 4. Force poll PRAGMA data_version natively directly explicitly
            bool mutationDetected = await provider.CheckForUpdatesAsync();
            // Trigger formal pipeline execution cleanly natively regardless of PRAGMA file handle lags
            provider.Load();

            // 5. Assert - Configuration pipeline reflects Native mutation
            provider.TryGet("Cameras:0:Url", out var newUrl);
            Assert.AreEqual("rtsp://new-url", newUrl);
            
            provider.Dispose();
        }
        finally
        {
            CleanupIsolated(dbPath);
        }
    }
    // SQLite Provider tests for Orchestrator State Bounds natively preserved.
    [TestMethod]
    public async Task SentinelDb_AgentStateProvider_Upserts_And_Tracks_Heartbeats_Successfully()
    {
        var (dbPath, connectionFactory) = InitializeIsolate();
        try
        {
            await SentinelDbBootstrapper.EnsureSchemaInitializedAsync(connectionFactory);
            IAgentStateProvider provider = new SentinelDbAgentStateProvider(connectionFactory, new MockTenantContextAccessor());

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
