using NeZha_Desktop.Models;

namespace NeZha_Desktop.Contracts;

public interface ITokenStore
{
    Task SaveSecretAsync(StoredAuthSecret secret);

    Task<StoredAuthSecret?> GetSecretAsync(string dashboardUrl, string username);

    Task DeleteSecretAsync(string dashboardUrl, string username);
}

