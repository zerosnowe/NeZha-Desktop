namespace NeZha_Desktop.Models;

public sealed class AuthSession
{
    public string DashboardUrl { get; init; } = string.Empty;

    public string Username { get; init; } = string.Empty;

    public string Token { get; init; } = string.Empty;

    public DateTimeOffset ExpireAtUtc { get; init; }

    public string Scheme { get; init; } = "Bearer";

    public bool IsPublicMode { get; init; }

    public bool CanRefresh { get; init; } = true;
}

