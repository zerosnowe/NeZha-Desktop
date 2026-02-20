using System.Net;
using System.Net.Http.Headers;
using NeZha_Desktop.Contracts;

namespace NeZha_Desktop.Infrastructure.Api;

public sealed class AuthHeaderHandler : DelegatingHandler
{
    private readonly IAuthSessionService _authSessionService;

    public AuthHeaderHandler(IAuthSessionService authSessionService)
    {
        _authSessionService = authSessionService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var session = _authSessionService.CurrentSession;
        if (session is not null && request.Headers.Authorization is null)
        {
            var token = await _authSessionService.GetValidTokenAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(session.Scheme, token);
            }
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || session is null || !session.CanRefresh)
        {
            return response;
        }

        response.Dispose();

        var refreshed = await _authSessionService.TryRefreshAsync(cancellationToken);
        if (!refreshed)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };
        }

        var retry = await CloneRequestAsync(request, cancellationToken);
        var latest = _authSessionService.CurrentSession;
        if (latest is not null)
        {
            retry.Headers.Authorization = new AuthenticationHeaderValue(latest.Scheme, latest.Token);
        }

        return await base.SendAsync(retry, cancellationToken);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(contentBytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var option in request.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        return clone;
    }
}

