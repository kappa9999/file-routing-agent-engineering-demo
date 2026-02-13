using System.Collections.Concurrent;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class RootAvailabilityTracker : IRootAvailabilityTracker
{
    private readonly ConcurrentDictionary<string, RootStateSnapshot> _states = new(StringComparer.OrdinalIgnoreCase);

    public RootAvailabilityState GetState(string rootPath)
    {
        return _states.TryGetValue(rootPath, out var snapshot)
            ? snapshot.State
            : RootAvailabilityState.Available;
    }

    public void MarkAvailable(string rootPath, string? note = null)
    {
        _states[rootPath] = new RootStateSnapshot(rootPath, RootAvailabilityState.Available, DateTime.UtcNow, note);
    }

    public void MarkUnavailable(string rootPath, string? note = null)
    {
        _states[rootPath] = new RootStateSnapshot(rootPath, RootAvailabilityState.Unavailable, DateTime.UtcNow, note);
    }

    public void MarkRecovering(string rootPath, string? note = null)
    {
        _states[rootPath] = new RootStateSnapshot(rootPath, RootAvailabilityState.Recovering, DateTime.UtcNow, note);
    }

    public IReadOnlyCollection<RootStateSnapshot> GetSnapshots()
    {
        return _states.Values.ToArray();
    }
}

