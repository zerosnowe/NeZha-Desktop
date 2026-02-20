using NeZha_Desktop.Models;

namespace NeZha_Desktop.Contracts;

public interface ITileNotificationService
{
    Task UpdateServerTilesAsync(IReadOnlyList<ServerSummary> servers);

    Task ClearAsync();
}
