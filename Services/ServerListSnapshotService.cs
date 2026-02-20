using NeZha_Desktop.Contracts;
using NeZha_Desktop.Models;

namespace NeZha_Desktop.Services;

public sealed class ServerListSnapshotService : IServerListSnapshotService
{
    private readonly object _sync = new();
    private List<ServerSummary> _snapshot = [];

    public event EventHandler? SnapshotChanged;

    public IReadOnlyList<ServerSummary> GetSnapshot()
    {
        lock (_sync)
        {
            return _snapshot.ToList();
        }
    }

    public void UpdateSnapshot(IReadOnlyList<ServerSummary> servers)
    {
        lock (_sync)
        {
            _snapshot = servers.ToList();
        }

        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }
}
