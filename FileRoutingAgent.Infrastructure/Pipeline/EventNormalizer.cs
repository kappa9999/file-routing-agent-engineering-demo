using System.Collections.Concurrent;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class EventNormalizer : IEventNormalizer
{
    private readonly ConcurrentDictionary<string, DateTime> _cooldownByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _renameClusterByKey = new(StringComparer.OrdinalIgnoreCase);

    public bool TryNormalize(
        DetectionCandidate candidate,
        TimeSpan cooldown,
        TimeSpan renameClusterWindow,
        out DetectionCandidate normalized)
    {
        var normalizedPath = candidate.SourcePath.Replace('/', '\\');
        normalized = candidate with { SourcePath = normalizedPath };

        var now = DateTime.UtcNow;
        var clusterKey = BuildClusterKey(normalizedPath);
        if (_renameClusterByKey.TryGetValue(clusterKey, out var clusterSeenUtc))
        {
            if (now - clusterSeenUtc < renameClusterWindow)
            {
                _renameClusterByKey[clusterKey] = now;
                return false;
            }
        }

        _renameClusterByKey[clusterKey] = now;

        if (_cooldownByPath.TryGetValue(normalizedPath, out var lastSeenUtc))
        {
            if (now - lastSeenUtc < cooldown)
            {
                return false;
            }
        }

        _cooldownByPath[normalizedPath] = now;
        CleanupStaleEntries(now, cooldown);
        return true;
    }

    private static string BuildClusterKey(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(path);
        return $"{directory}|{baseName}";
    }

    private void CleanupStaleEntries(DateTime now, TimeSpan cooldown)
    {
        var cutoff = now - TimeSpan.FromTicks(cooldown.Ticks * 3);
        foreach (var (key, value) in _cooldownByPath)
        {
            if (value < cutoff)
            {
                _cooldownByPath.TryRemove(key, out _);
            }
        }

        foreach (var (key, value) in _renameClusterByKey)
        {
            if (value < now - TimeSpan.FromMinutes(5))
            {
                _renameClusterByKey.TryRemove(key, out _);
            }
        }
    }
}
