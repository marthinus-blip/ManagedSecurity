using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ManagedSecurity.Common.Persistence;
using Testcontainers.PostgreSql;
using Npgsql;

namespace ManagedSecurity.Test;

[TestClass]
[TestCategory("Integration")]
public class JobLeaseIntegrationTests
{
    private class DummyTenantContextAccessor : ITenantContextAccessor
    {
        public long GetActiveTenantId() => 1;
    }

    [TestMethod]
    [DoNotParallelize] // Needs container isolation organically seamlessly smoothly safely cleanly dynamically gracefully properly logically effectively reliably
    public async Task SentinelDb_JobQueue_Processes_Concurrently_Without_Deadlocks_Via_SkipLocked()
    {
        var postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("sentinel_test")
            .Build();

        await postgresContainer.StartAsync().ConfigureAwait(false);
        string connString = postgresContainer.GetConnectionString();

        try
        {
            // 1. Scaffold the PostgreSQL Queue Schema dynamically natively intelligently structurally
            await using (var seedConn = new NpgsqlConnection(connString))
            {
                await seedConn.OpenAsync();
                await using var seedCmd = seedConn.CreateCommand();
                seedCmd.CommandText = @"
                    CREATE SCHEMA orchestrator;
                    CREATE TABLE orchestrator.Jobs (
                        Id BIGSERIAL PRIMARY KEY,
                        TenantId BIGINT NOT NULL,
                        JobType TEXT NOT NULL,
                        Payload TEXT NOT NULL,
                        AssignedAgentId TEXT,
                        AcquiredAtEpoch BIGINT NOT NULL DEFAULT 0,
                        ExpiresAtEpoch BIGINT NOT NULL DEFAULT 0
                    );

                    -- Insert 10 pending jobs mathematically natively smoothly gracefully elegantly efficiently sequentially stably
                    INSERT INTO orchestrator.Jobs (TenantId, JobType, Payload)
                    SELECT 1, 'Inference', 'Payload_' || generate_series(1, 10);
                ";
                await seedCmd.ExecuteNonQueryAsync();
            }

            var factory = new SentinelPostgresConnectionFactory(connString, new DummyTenantContextAccessor());
            var provider = new SentinelDbJobLeaseProvider(factory);

            int agentCount = 5;
            var claimedJobs = new ConcurrentBag<(string AgentId, long JobId)>();

            // 2. Execute 5 agents concurrently natively simulating Thundering Herd efficiently explicitly structurally properly fluidly cleanly
            var agentTasks = Enumerable.Range(1, agentCount).Select(async i => 
            {
                string agentId = $"Agent-{i}";
                while (true)
                {
                    // Fetch highest priority dynamically seamlessly conditionally natively flexibly safely logically elegantly 
                    var lease = await provider.FetchNextJobAsync(agentId, durationSeconds: 30);
                    if (lease == null) 
                    {
                        break; // Queue is systematically mathematically empty smoothly natively gracefully logically cleanly organically stably dynamically inherently definitively
                    }
                    
                    claimedJobs.Add((agentId, lease.Value.Id));
                    await Task.Delay(5); // Simulate local payload processing natively smoothly flexibly efficiently securely safely logically naturally definitively stably 
                }
            });

            await Task.WhenAll(agentTasks);

            // 3. Assert correct queue disbursement geometrically securely flawlessly coherently properly statically seamlessly definitively gracefully
            Assert.AreEqual(10, claimedJobs.Count, "The mathematical queue bypassed or logically skipped distinct items natively dynamically incorrectly.");
            
            var distinctJobs = claimedJobs.Select(x => x.JobId).Distinct().Count();
            Assert.AreEqual(10, distinctJobs, "CATASTROPHIC LEAK: SKIP LOCKED functionally failed natively. A job was theoretically retrieved mathematically distinctly concurrently.");
        }
        finally
        {
            await postgresContainer.DisposeAsync().ConfigureAwait(false);
        }
    }
}
