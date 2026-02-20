using NeZha_Desktop.Models;

namespace NeZha_Desktop.Contracts;

public interface IPanelProfileStore
{
    Task<IReadOnlyList<DashboardProfile>> GetProfilesAsync();

    Task SaveOrUpdateAsync(DashboardProfile profile);

    Task<DashboardProfile?> GetLastUsedAsync();

    Task SetLastUsedAsync(string profileId);
}

