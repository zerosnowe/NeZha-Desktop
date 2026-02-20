using NeZha_Desktop.Contracts;
using NeZha_Desktop.Infrastructure.Api;
using NeZha_Desktop.Models;
using Serilog;

namespace NeZha_Desktop.Services;

public sealed class AuthSessionService : IAuthSessionService
{
    private const string PublicModeUser = "__public__";

    private readonly IAuthApiClient _authApiClient;
    private readonly ITokenStore _tokenStore;
    private readonly IPanelProfileStore _panelProfileStore;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AuthSessionService(IAuthApiClient authApiClient, ITokenStore tokenStore, IPanelProfileStore panelProfileStore)
    {
        _authApiClient = authApiClient;
        _tokenStore = tokenStore;
        _panelProfileStore = panelProfileStore;
    }

    public AuthSession? CurrentSession { get; private set; }

    public event EventHandler<AuthSession?>? SessionChanged;

    public async Task<AuthSession?> TryAutoSignInAsync(CancellationToken cancellationToken)
    {
        var last = await _panelProfileStore.GetLastUsedAsync();
        if (last == null || !last.RememberLogin)
        {
            return null;
        }

        if (last.PublicMode)
        {
            var secret = await _tokenStore.GetSecretAsync(last.DashboardUrl, PublicModeUser);
            return await EnterPublicModeAsync(last.DashboardUrl, secret?.Token, true, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(last.Username))
        {
            return null;
        }

        var stored = await _tokenStore.GetSecretAsync(last.DashboardUrl, last.Username);
        if (stored == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(stored.Token) && stored.ExpireAtUtc.HasValue)
        {
            var cached = new AuthSession
            {
                DashboardUrl = AuthApiClient.NormalizeDashboardUrl(stored.DashboardUrl),
                Username = stored.Username,
                Token = stored.Token,
                ExpireAtUtc = stored.ExpireAtUtc.Value,
                Scheme = stored.Scheme,
                CanRefresh = true,
            };

            CurrentSession = cached;
            NotifySessionChanged();

            if (cached.ExpireAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return cached;
            }

            if (await TryRefreshAsync(cancellationToken))
            {
                return CurrentSession;
            }
        }

        if (!string.IsNullOrWhiteSpace(stored.Password))
        {
            try
            {
                return await SignInAsync(stored.DashboardUrl, stored.Username, stored.Password, true, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Auto sign in failed for {Dashboard}", stored.DashboardUrl);
            }
        }

        await SignOutAsync();
        return null;
    }

    public async Task<AuthSession> SignInAsync(
        string dashboardUrl,
        string username,
        string password,
        bool rememberLogin,
        CancellationToken cancellationToken)
    {
        var normalizedUrl = AuthApiClient.NormalizeDashboardUrl(dashboardUrl);
        ValidateDashboardUrl(normalizedUrl);

        var session = await _authApiClient.LoginAsync(normalizedUrl, username.Trim(), password, cancellationToken);

        CurrentSession = new AuthSession
        {
            DashboardUrl = session.DashboardUrl,
            Username = session.Username,
            Token = session.Token,
            ExpireAtUtc = session.ExpireAtUtc,
            Scheme = session.Scheme,
            IsPublicMode = false,
            CanRefresh = true,
        };
        NotifySessionChanged();

        await SaveProfileAsync(normalizedUrl, username.Trim(), publicMode: false, rememberLogin);

        if (rememberLogin)
        {
            await _tokenStore.SaveSecretAsync(new StoredAuthSecret
            {
                DashboardUrl = normalizedUrl,
                Username = username.Trim(),
                Password = password,
                Token = session.Token,
                ExpireAtUtc = session.ExpireAtUtc,
                Scheme = session.Scheme,
            });
        }
        else
        {
            await _tokenStore.DeleteSecretAsync(normalizedUrl, username.Trim());
        }

        return CurrentSession;
    }

    public async Task<AuthSession> EnterPublicModeAsync(
        string dashboardUrl,
        string? apiToken,
        bool rememberLogin,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var normalizedUrl = AuthApiClient.NormalizeDashboardUrl(dashboardUrl);
        ValidateDashboardUrl(normalizedUrl);

        CurrentSession = new AuthSession
        {
            DashboardUrl = normalizedUrl,
            Username = PublicModeUser,
            Token = apiToken?.Trim() ?? string.Empty,
            ExpireAtUtc = DateTimeOffset.MaxValue,
            Scheme = "Token",
            IsPublicMode = true,
            CanRefresh = false,
        };

        NotifySessionChanged();

        await SaveProfileAsync(normalizedUrl, PublicModeUser, publicMode: true, rememberLogin);

        if (rememberLogin && !string.IsNullOrWhiteSpace(CurrentSession.Token))
        {
            await _tokenStore.SaveSecretAsync(new StoredAuthSecret
            {
                DashboardUrl = normalizedUrl,
                Username = PublicModeUser,
                Token = CurrentSession.Token,
                Scheme = CurrentSession.Scheme,
            });
        }
        else
        {
            await _tokenStore.DeleteSecretAsync(normalizedUrl, PublicModeUser);
        }

        return CurrentSession;
    }

    public async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
    {
        if (CurrentSession == null || !CurrentSession.CanRefresh)
        {
            return false;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (CurrentSession.ExpireAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return true;
            }

            var refreshed = await _authApiClient.RefreshTokenAsync(CurrentSession, cancellationToken);
            CurrentSession = new AuthSession
            {
                DashboardUrl = refreshed.DashboardUrl,
                Username = refreshed.Username,
                Token = refreshed.Token,
                ExpireAtUtc = refreshed.ExpireAtUtc,
                Scheme = refreshed.Scheme,
                IsPublicMode = false,
                CanRefresh = true,
            };
            NotifySessionChanged();

            var secret = await _tokenStore.GetSecretAsync(refreshed.DashboardUrl, refreshed.Username);
            if (secret != null)
            {
                await _tokenStore.SaveSecretAsync(new StoredAuthSecret
                {
                    DashboardUrl = secret.DashboardUrl,
                    Username = secret.Username,
                    Password = secret.Password,
                    Token = refreshed.Token,
                    ExpireAtUtc = refreshed.ExpireAtUtc,
                    Scheme = refreshed.Scheme,
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Refresh token failed");
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<string?> GetValidTokenAsync(CancellationToken cancellationToken)
    {
        if (CurrentSession == null)
        {
            return null;
        }

        if (!CurrentSession.CanRefresh)
        {
            return CurrentSession.Token;
        }

        if (CurrentSession.ExpireAtUtc <= DateTimeOffset.UtcNow.AddMinutes(2))
        {
            var ok = await TryRefreshAsync(cancellationToken);
            if (!ok)
            {
                return null;
            }
        }

        return CurrentSession.Token;
    }

    public async Task SignOutAsync()
    {
        if (CurrentSession != null)
        {
            await _tokenStore.DeleteSecretAsync(CurrentSession.DashboardUrl, CurrentSession.Username);
        }

        CurrentSession = null;
        NotifySessionChanged();
    }

    private async Task SaveProfileAsync(string dashboardUrl, string username, bool publicMode, bool rememberLogin)
    {
        var existing = (await _panelProfileStore.GetProfilesAsync()).FirstOrDefault(p =>
            p.DashboardUrl.Equals(dashboardUrl, StringComparison.OrdinalIgnoreCase) &&
            p.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        var profile = existing ?? new DashboardProfile();
        profile.DashboardUrl = dashboardUrl;
        profile.Username = username;
        profile.PublicMode = publicMode;
        profile.RememberLogin = rememberLogin;
        profile.LastUsedUtc = DateTimeOffset.UtcNow;

        await _panelProfileStore.SaveOrUpdateAsync(profile);
    }

    private static void ValidateDashboardUrl(string url)
    {
#if !DEBUG
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("生产模式仅支持 HTTPS Dashboard URL。");
        }
#endif
    }

    private void NotifySessionChanged()
    {
        SessionChanged?.Invoke(this, CurrentSession);
    }
}
