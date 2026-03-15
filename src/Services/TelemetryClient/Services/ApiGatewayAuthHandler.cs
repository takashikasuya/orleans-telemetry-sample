using System.Net.Http.Headers;

namespace TelemetryClient.Services;

public sealed class ApiGatewayAuthHandler : DelegatingHandler
{
    private readonly OidcTokenProvider _tokenProvider;
    private readonly ILogger<ApiGatewayAuthHandler> _logger;

    public ApiGatewayAuthHandler(OidcTokenProvider tokenProvider, ILogger<ApiGatewayAuthHandler> logger)
    {
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            _logger.LogDebug("No OIDC token available for ApiGateway request {Method} {Url}.", request.Method, request.RequestUri);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
