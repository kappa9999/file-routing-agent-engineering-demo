using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace FileRoutingAgent.Infrastructure.Watching;

public sealed class SourceWatcher(
    RuntimeConfigSnapshotAccessor snapshotAccessor,
    IPathCanonicalizer canonicalizer,
    IRootAvailabilityTracker rootAvailabilityTracker,
    ILogger<SourceWatcher> logger) : ISourceWatcher
{
    private readonly List<FileSystemWatcher> _watchers = new();

    public Task StartAsync(
        Func<DetectionCandidate, ValueTask> onCandidate,
        Func<string, ValueTask>? onOverflow,
        CancellationToken cancellationToken)
    {
        var snapshot = snapshotAccessor.Snapshot
            ?? throw new InvalidOperationException("Runtime snapshot not loaded.");

        var roots = snapshot.Policy.Monitoring.WatchRoots
            .Concat(snapshot.Policy.Monitoring.CandidateRoots)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var root in roots)
        {
            var canonicalRoot = canonicalizer.Canonicalize(root);
            if (!Directory.Exists(canonicalRoot))
            {
                rootAvailabilityTracker.MarkUnavailable(canonicalRoot, "Watch root unavailable at startup.");
                logger.LogWarning("Watch root not found at startup: {Root}", canonicalRoot);
                continue;
            }

            rootAvailabilityTracker.MarkAvailable(canonicalRoot, "Watcher attached.");

            var watcher = new FileSystemWatcher(canonicalRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
                EnableRaisingEvents = true
            };

            watcher.Changed += (_, args) => _ = EmitAsync(args.FullPath, DetectionSource.WatcherHint, onCandidate);
            watcher.Created += (_, args) => _ = EmitAsync(args.FullPath, DetectionSource.WatcherHint, onCandidate);
            watcher.Renamed += (_, args) =>
            {
                _ = EmitAsync(args.OldFullPath, DetectionSource.WatcherHint, onCandidate);
                _ = EmitAsync(args.FullPath, DetectionSource.WatcherHint, onCandidate);
            };
            watcher.Error += async (_, _) =>
            {
                rootAvailabilityTracker.MarkRecovering(canonicalRoot, "Watcher overflow.");
                logger.LogWarning("Watcher overflow or failure in root {Root}.", canonicalRoot);
                if (onOverflow is not null)
                {
                    await onOverflow(canonicalRoot);
                }
            };

            _watchers.Add(watcher);
            logger.LogInformation("Watching root: {Root}", canonicalRoot);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        return ValueTask.CompletedTask;
    }

    private async ValueTask EmitAsync(
        string path,
        DetectionSource source,
        Func<DetectionCandidate, ValueTask> onCandidate)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var canonicalPath = canonicalizer.Canonicalize(path);
        logger.LogDebug("Watcher event: {Source} {Path}", source, canonicalPath);
        var candidate = new DetectionCandidate(canonicalPath, source, DateTime.UtcNow);
        await onCandidate(candidate);
    }
}
