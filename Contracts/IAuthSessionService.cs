using NeZha_Desktop.Models;

namespace NeZha_Desktop.Contracts;

public interface IAuthSessionService
{
    AuthSession? CurrentSession { get; }

    event EventHandler<AuthSession?>? SessionChanged;

    Task<AuthSession?> TryAutoSignInAsync(CancellationToken cancellationToken);

    Task<AuthSession> SignInAsync(
        string dashboardUrl,
        string username,
        string password,
        bool rememberLogin,
        CancellationToken cancellationToken);

    Task<AuthSession> EnterPublicModeAsync(
        string dashboardUrl,
        string? apiToken,
        bool rememberLogin,
        CancellationToken cancellationToken);

    Task<bool> TryRefreshAsync(CancellationToken cancellationToken);

    Task<string?> GetValidTokenAsync(CancellationToken cancellationToken);

    Task SignOutAsync();
}

