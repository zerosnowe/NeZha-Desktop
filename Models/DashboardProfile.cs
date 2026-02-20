namespace NeZha_Desktop.Models;

public sealed class DashboardProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DashboardUrl { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public bool PublicMode { get; set; }

    public bool RememberLogin { get; set; }

    public DateTimeOffset LastUsedUtc { get; set; } = DateTimeOffset.UtcNow;
}

