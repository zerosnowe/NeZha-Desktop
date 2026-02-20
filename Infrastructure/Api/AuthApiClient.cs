using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NeZha_Desktop.Contracts;
using NeZha_Desktop.Models;

namespace NeZha_Desktop.Infrastructure.Api;

public sealed class AuthApiClient : IAuthApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    private const string LoginPath = "/api/v1/login";
    private const string RefreshPath = "/api/v1/refresh-token";

    public AuthApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<AuthSession> LoginAsync(
        string dashboardUrl,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var client = BuildClient(dashboardUrl);

        var response = await client.PostAsJsonAsync(
            LoginPath,
            new LoginApiRequest { Username = username, Password = password },
            cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LoginApiData>>(cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success || envelope.Data is null)
        {
            throw new InvalidOperationException(envelope?.Error ?? $"登录失败，HTTP {(int)response.StatusCode}");
        }

        return BuildSession(dashboardUrl, username, envelope.Data, "Bearer");
    }

    public async Task<AuthSession> RefreshTokenAsync(AuthSession session, CancellationToken cancellationToken)
    {
        var client = BuildClient(session.DashboardUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(session.Scheme, session.Token);

        var response = await client.GetAsync(RefreshPath, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LoginApiData>>(cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success || envelope.Data is null)
        {
            throw new InvalidOperationException(envelope?.Error ?? "刷新 Token 失败");
        }

        return BuildSession(session.DashboardUrl, session.Username, envelope.Data, session.Scheme);
    }

    private HttpClient BuildClient(string dashboardUrl)
    {
        var client = _httpClientFactory.CreateClient("NezhaAuth");
        client.BaseAddress = new Uri(NormalizeDashboardUrl(dashboardUrl));
        return client;
    }

    private static AuthSession BuildSession(string dashboardUrl, string username, LoginApiData data, string scheme)
    {
        if (!DateTimeOffset.TryParse(data.Expire, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expireAt))
        {
            expireAt = DateTimeOffset.UtcNow.AddHours(1);
        }

        return new AuthSession
        {
            DashboardUrl = NormalizeDashboardUrl(dashboardUrl),
            Username = username,
            Token = data.Token,
            ExpireAtUtc = expireAt.ToUniversalTime(),
            Scheme = scheme,
        };
    }

    public static string NormalizeDashboardUrl(string value)
    {
        var url = value.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = $"https://{url}";
        }

        return url.TrimEnd('/');
    }
}

