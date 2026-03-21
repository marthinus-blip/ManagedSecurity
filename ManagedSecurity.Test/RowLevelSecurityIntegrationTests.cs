using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ManagedSecurity.Common.Persistence;
using Testcontainers.PostgreSql;
using Npgsql;

namespace ManagedSecurity.Test;

[TestClass]
[TestCategory("Integration")]
public class RowLevelSecurityIntegrationTests
{
    // The DI Mock replicating ASP.NET Core HttpContext behavior
    private class MockTenantContextAccessor : ITenantContextAccessor
    {
        public long ActiveTenantId { get; set; }
        public long GetActiveTenantId() => ActiveTenantId;
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task SentinelPostgresConnectionFactory_Injects_RLS_Successfully_Preventing_Tuple_Bleed()
    {
        // 1. Boot the ephemeral PostgreSQL container identically matching com_proj physical deployment.
        // This spins up a real database engine exclusively for this test method.
        var postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("sentinel_test")
            .Build();

        await postgresContainer.StartAsync().ConfigureAwait(false);
        string connString = postgresContainer.GetConnectionString();

        try
        {
            // 2. Scaffold the RLS Schema directly into the blank container 
            await using (var seedConn = new NpgsqlConnection(connString))
            {
                await seedConn.OpenAsync();
                await using var seedCmd = seedConn.CreateCommand();
                seedCmd.CommandText = @"
                    CREATE SCHEMA auth;
                    CREATE TABLE auth.Cameras (
                        Id BIGSERIAL PRIMARY KEY,
                        TenantId BIGINT NOT NULL,
                        Name TEXT NOT NULL
                    );
                    -- The Zero-Trust Perimeter Enablement:
                    ALTER TABLE auth.Cameras ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE auth.Cameras FORCE ROW LEVEL SECURITY;
                    -- The physical policy restricting access natively mapping to ActiveTenantId:
                    CREATE POLICY isolation_policy ON auth.Cameras 
                        USING (TenantId = current_setting('app.current_tenant_id', true)::BIGINT);

                    -- Strip physical SUPERUSER privileges natively for the test assertions:
                    CREATE ROLE test_rw NOLOGIN;
                    GRANT USAGE ON SCHEMA auth TO test_rw;
                    GRANT ALL ON auth.Cameras TO test_rw;
                    GRANT USAGE, SELECT ON SEQUENCE auth.cameras_id_seq TO test_rw;
                ";
                await seedCmd.ExecuteNonQueryAsync();
            }

            // 3. Initiate the Dependency Injection bounds
            var tenantAccessor = new MockTenantContextAccessor { ActiveTenantId = 100 }; // Logging in as Tenant 100
            
            // This is the core architectural component we are actively validating
            var factory = new SentinelPostgresConnectionFactory(connString, tenantAccessor);

            // 4. Act: Insert Camera for Tenant 100 securely utilizing the factory
            await using (var tenant100Conn = await factory.CreateConnectionAsync())
            {
                await using var cmd100 = tenant100Conn.CreateCommand();
                cmd100.CommandText = "SET LOCAL ROLE test_rw; INSERT INTO auth.Cameras (TenantId, Name) VALUES (100, 'Lobby_Camera');";
                await cmd100.ExecuteNonQueryAsync();
            }

            // 5. Assert: Tenant 200 attempts to read the camera. They should see 0 rows.
            tenantAccessor.ActiveTenantId = 200; // Physically switch the JWT/Context scope to Tenant 200

            await using (var tenant200Conn = await factory.CreateConnectionAsync())
            {
                await using var cmd200 = tenant200Conn.CreateCommand();
                cmd200.CommandText = "SET LOCAL ROLE test_rw; SELECT COUNT(*) FROM auth.Cameras;";
                var count = (long)await cmd200.ExecuteScalarAsync();

                Assert.AreEqual(0, count, "RLS FAILED! Tenant 200 successfully retrieved Tenant 100's camera data.");
            }
            
            // 6. Assert: Tenant 100 can read its own camera cleanly.
            tenantAccessor.ActiveTenantId = 100; // Revert to Tenant 100

            await using (var final100Conn = await factory.CreateConnectionAsync())
            {
                await using var final100Cmd = final100Conn.CreateCommand();
                final100Cmd.CommandText = "SET LOCAL ROLE test_rw; SELECT COUNT(*) FROM auth.Cameras;";
                var count = (long)await final100Cmd.ExecuteScalarAsync();

                Assert.AreEqual(1, count, "RLS FAILED! Tenant 100 could not retrieve its own localized data.");
            }
        }
        finally
        {
            // Guaranteed cleanup ensures the docker daemon natively wipes the container terminating the DB.
            await postgresContainer.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    [DoNotParallelize] // Needs monopoly over the ADO.NET connection pool to guarantee we pull the identical underlying Postgres socket.
    public async Task Npgsql_Connection_Pool_Mathematically_Scrubs_Tenant_Context_Via_Discard_All_Automatically()
    {
        var postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("sentinel_test")
            .Build();

        await postgresContainer.StartAsync().ConfigureAwait(false);
        string connString = postgresContainer.GetConnectionString();

        try
        {
            var tenantAccessor = new MockTenantContextAccessor { ActiveTenantId = 999 };
            var factory = new SentinelPostgresConnectionFactory(connString, tenantAccessor);
            
            int backendProcessId;

            // Phase 1: Check out via Factory which structurally issues `SET app.current_tenant_id = 999;`
            await using (var leasedConn = (NpgsqlConnection)await factory.CreateConnectionAsync())
            {
                backendProcessId = leasedConn.ProcessID; // Identify the physical PostgreSQL thread lock under the hood.

                await using var cmd = leasedConn.CreateCommand();
                cmd.CommandText = "SELECT current_setting('app.current_tenant_id', true);";
                var setting = (string)await cmd.ExecuteScalarAsync();
                Assert.AreEqual("999", setting, "Factory failed to establish identity bound.");
            } // leasedConn.DisposeAsync() pushes socket back into the pool, activating DISCARD ALL implicitly.

            // Phase 2: Open a raw ADO.NET connection completely bypassing the Factory bounds natively.
            await using (var rogueConn = new NpgsqlConnection(connString))
            {
                await rogueConn.OpenAsync();
                
                // Assert it is mathematically identical hardware socket to prove the pool recycled it actively.
                Assert.AreEqual(backendProcessId, rogueConn.ProcessID, "ADO.NET did not physically recycle the underlying PostgreSQL socket.");

                await using var rogueCmd = rogueConn.CreateCommand();
                rogueCmd.CommandText = "SELECT current_setting('app.current_tenant_id', true);";
                var rogueSetting = (string)await rogueCmd.ExecuteScalarAsync();
                
                // The Session State string must be completely scrubbed ("") natively by the pooling algorithm!
                Assert.AreEqual("", rogueSetting, "CATASTROPHIC LEAK: Tenant ID context bled structurally across the ADO.NET Connection Pool!");
            }
        }
        finally
        {
            await postgresContainer.DisposeAsync().ConfigureAwait(false);
        }
    }
}
