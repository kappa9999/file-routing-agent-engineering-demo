using FileRoutingAgent.Core.Configuration;

namespace FileRoutingAgent.Infrastructure.Configuration;

public sealed class RuntimeConfigSnapshotAccessor
{
    private readonly object _sync = new();
    private RuntimeConfigSnapshot? _snapshot;

    public RuntimeConfigSnapshot? Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public void Update(RuntimeConfigSnapshot snapshot)
    {
        lock (_sync)
        {
            _snapshot = snapshot;
        }
    }
}

