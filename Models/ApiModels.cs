namespace NeZha_Desktop.Models;

public sealed class LoginApiRequest
{
    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}

public sealed class LoginApiData
{
    public string Token { get; set; } = string.Empty;

    public string Expire { get; set; } = string.Empty;
}

public sealed class ApiEnvelope<T>
{
    public bool Success { get; set; }

    public T? Data { get; set; }

    public string? Error { get; set; }
}

