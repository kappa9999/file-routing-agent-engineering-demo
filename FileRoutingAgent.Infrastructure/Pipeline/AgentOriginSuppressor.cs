using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Infrastructure.Configuration;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class AgentOriginSuppressor(
    IAuditStore auditStore,
    RuntimeConfigSnapshotAccessor snapshotAccessor) : IAgentOriginSuppressor
{
    public async Task<bool> ShouldSuppressAsync(DetectionCandidate candidate, CancellationToken cancellationToken)
    {
        if (!File.Exists(candidate.SourcePath))
        {
            return false;
        }

        var fileInfo = new FileInfo(candidate.SourcePath);
        if (!fileInfo.Exists)
        {
            return false;
        }

        var snapshot = snapshotAccessor.Snapshot;
        if (snapshot is null)
        {
            return false;
        }

        var ttl = TimeSpan.FromMinutes(Math.Max(1, snapshot.Policy.Suppression.RecentOperationTtlMinutes));
        return await auditStore.IsRecentOperationAsync(
            candidate.SourcePath,
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc,
            ttl,
            cancellationToken);
    }
}
