using NeZha_Desktop.Models;

namespace NeZha_Desktop.Contracts;

public interface IServerApiClient
{
    Task<IReadOnlyList<ServerSummary>> GetServersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ServerGroupSummary>> GetServerGroupsAsync(CancellationToken cancellationToken);

    Task<ServerDetail> GetServerDetailAsync(ulong serverId, CancellationToken cancellationToken);

    Task<IReadOnlyList<NetworkMonitorSummary>> GetServerNetworkMonitorsAsync(ulong serverId, string period, CancellationToken cancellationToken);
}
