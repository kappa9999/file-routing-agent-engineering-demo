using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace FileRoutingAgent.Infrastructure.Watching;

public sealed class ReconciliationScanner(
    RuntimeConfigSnapshotAccessor snapshotAccessor,
    IAuditStore auditStore,
    IPathCanonicalizer canonicalizer,
    IRootAvailabilityTracker rootAvailabilityTracker,
    ILogger<ReconciliationScanner> logger) : IReconciliationScanner
{
    public async Task<ScanRun> RunScanAsync(
        Func<DetectionCandidate, ValueTask> onCandidate,
        bool priority,
        CancellationToken cancellationToken)
    {
        var snapshot = snapshotAccessor.Snapshot
            ?? throw new InvalidOperationException("Runtime snapshot not loaded.");

        var startedUtc = DateTime.UtcNow;
        var roots = priority
            ? snapshot.Policy.Monitoring.CandidateRoots.Concat(snapshot.Policy.Monitoring.WatchRoots)
            : snapshot.Policy.Monitoring.CandidateRoots;

        var distinctRoots = roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var found = 0;
        var queued = 0;
        var skipped = 0;
        var errors = 0;

        foreach (var root in distinctRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var canonicalRoot = canonicalizer.Canonicalize(root);
            if (!Directory.Exists(canonicalRoot))
            {
                errors++;
                rootAvailabilityTracker.MarkUnavailable(canonicalRoot, "Root not available during scan.");
                logger.LogWarning("Scan root unavailable: {Root}", canonicalRoot);
                continue;
            }

            rootAvailabilityTracker.MarkAvailable(canonicalRoot, "Root available for scan.");

            RootWatermark? watermark = null;
            try
            {
                watermark = await auditStore.GetWatermarkAsync(canonicalRoot, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to load watermark for root {Root}", canonicalRoot);
            }

            if (watermark is null)
            {
                // Bootstrap without replaying historical files on first run.
                await auditStore.SaveWatermarkAsync(
                    new RootWatermark(canonicalRoot, DateTime.UtcNow, null),
                    cancellationToken);
                logger.LogInformation("Initialized watermark for root {Root}; skipping historical backlog.", canonicalRoot);
                continue;
            }

            var scanCutoffUtc = watermark.LastScanUtc.AddSeconds(-5);
            var newestSeenUtc = watermark.LastScanUtc;
            string? lastSeenPath = watermark.LastSeenPath;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(
                    canonicalRoot,
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.System
                    });
            }
            catch (Exception exception)
            {
                errors++;
                logger.LogWarning(exception, "Failed to enumerate root {Root}", canonicalRoot);
                continue;
            }

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                found++;

                if (!ShouldConsider(filePath, snapshot.Policy.ManagedExtensions))
                {
                    skipped++;
                    continue;
                }

                DateTime lastWriteUtc;
                long sizeBytes;
                try
                {
                    var info = new FileInfo(filePath);
                    if (!info.Exists)
                    {
                        skipped++;
                        continue;
                    }

                    lastWriteUtc = info.LastWriteTimeUtc;
                    sizeBytes = info.Length;
                }
                catch
                {
                    skipped++;
                    continue;
                }

                if (lastWriteUtc < scanCutoffUtc)
                {
                    skipped++;
                    continue;
                }

                var canonicalPath = canonicalizer.Canonicalize(filePath);
                await onCandidate(new DetectionCandidate(
                    canonicalPath,
                    DetectionSource.ReconciliationScan,
                    DateTime.UtcNow,
                    sizeBytes,
                    lastWriteUtc));

                queued++;
                if (lastWriteUtc > newestSeenUtc)
                {
                    newestSeenUtc = lastWriteUtc;
                    lastSeenPath = canonicalPath;
                }
            }

            try
            {
                await auditStore.SaveWatermarkAsync(
                    new RootWatermark(canonicalRoot, DateTime.UtcNow, lastSeenPath),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                errors++;
                logger.LogWarning(exception, "Failed to save watermark for root {Root}", canonicalRoot);
            }
        }

        return new ScanRun(
            startedUtc,
            DateTime.UtcNow,
            "ALL",
            found,
            queued,
            skipped,
            errors);
    }

    private static bool ShouldConsider(string path, IReadOnlyCollection<string> managedExtensions)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return managedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
