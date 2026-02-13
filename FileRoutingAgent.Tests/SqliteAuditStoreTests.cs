using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FileRoutingAgent.Tests;

public sealed class SqliteAuditStoreTests
{
    [Fact]
    public async Task GetRecentQueries_ReturnMostRecentEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "FileRoutingAgentTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var dbPath = Path.Combine(root, "state.db");
            var options = Options.Create(new AgentRuntimeOptions
            {
                DatabasePath = dbPath
            });

            var store = new SqliteAuditStore(options, NullLogger<SqliteAuditStore>.Instance);
            await store.InitializeAsync(CancellationToken.None);

            await store.WriteEventAsync(new AuditEvent(DateTime.UtcNow.AddSeconds(-2), "a"), CancellationToken.None);
            await store.WriteEventAsync(new AuditEvent(DateTime.UtcNow.AddSeconds(-1), "b"), CancellationToken.None);
            await store.RecordScanRunAsync(new ScanRun(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, "R1", 1, 1, 0, 0), CancellationToken.None);
            await store.RecordScanRunAsync(new ScanRun(DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(-1), "R2", 2, 1, 1, 0), CancellationToken.None);

            var events = await store.GetRecentAuditEventsAsync(1, CancellationToken.None);
            var scans = await store.GetRecentScanRunsAsync(1, CancellationToken.None);

            Assert.Single(events);
            Assert.Equal("b", events[0].EventType);
            Assert.Single(scans);
        }
        finally
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (!Directory.Exists(root))
                {
                    break;
                }

                try
                {
                    Directory.Delete(root, true);
                    break;
                }
                catch (IOException)
                {
                    await Task.Delay(100);
                }
            }
        }
    }
}
