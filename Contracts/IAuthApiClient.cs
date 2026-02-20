using NeZha_Desktop.Models;

namespace NeZha_Desktop.Contracts;

public interface IAuthApiClient
{
    Task<AuthSession> LoginAsync(string dashboardUrl, string username, string password, CancellationToken cancellationToken);

    Task<AuthSession> RefreshTokenAsync(AuthSession session, CancellationToken cancellationToken);
}

