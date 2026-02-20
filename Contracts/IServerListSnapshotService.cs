using NeZha_Desktop.Models;

namespace NeZha_Desktop.Contracts;

public interface IServerListSnapshotService
{
    event EventHandler? SnapshotChanged;

    IReadOnlyList<ServerSummary> GetSnapshot();

    void UpdateSnapshot(IReadOnlyList<ServerSummary> servers);
}
