using System.Collections.Concurrent;
using FileRoutingAgent.Core.Interfaces;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class ScanScheduler : IScanScheduler
{
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly ConcurrentDictionary<string, byte> _dedupe = new(StringComparer.OrdinalIgnoreCase);

    public void RequestPriorityScan(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        if (_dedupe.TryAdd(rootPath, 0))
        {
            _queue.Enqueue(rootPath);
        }
    }

    public bool TryDequeuePriorityScan(out string? rootPath)
    {
        if (_queue.TryDequeue(out var dequeued))
        {
            _dedupe.TryRemove(dequeued, out _);
            rootPath = dequeued;
            return true;
        }

        rootPath = null;
        return false;
    }
}
