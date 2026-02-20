namespace NeZha_Desktop.Models;

public sealed class StoredAuthSecret
{
    public string DashboardUrl { get; init; } = string.Empty;

    public string Username { get; init; } = string.Empty;

    public string? Password { get; init; }

    public string? Token { get; init; }

    public DateTimeOffset? ExpireAtUtc { get; init; }

    public string Scheme { get; init; } = "Bearer";
}

