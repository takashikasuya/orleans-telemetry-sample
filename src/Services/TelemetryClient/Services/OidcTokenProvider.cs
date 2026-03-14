using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TelemetryClient.Services;

public sealed class OidcTokenProvider
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly TelemetryClientOidcOptions _options;
    private readonly ILogger<OidcTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public OidcTokenProvider(
        IHttpClientFactory clientFactory,
        IOptions<TelemetryClientOidcOptions> options,
        ILogger<OidcTokenProvider> logger)
    {
        _clientFactory = clientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (IsTokenValid())
        {
            _logger.LogDebug("Using cached OIDC token");
            return _accessToken;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsTokenValid())
            {
                _logger.LogDebug("Using cached OIDC token (after lock)");
                return _accessToken;
            }

            if (string.IsNullOrWhiteSpace(_options.Authority))
            {
                _logger.LogWarning("OIDC authority is not configured.");
                return null;
            }

            var tokenEndpoint = _options.TokenEndpoint;
            if (string.IsNullOrWhiteSpace(tokenEndpoint))
            {
                tokenEndpoint = $"{_options.Authority.TrimEnd('/')}/token";
            }

            _logger.LogInformation("Requesting OIDC token from {TokenEndpoint}", tokenEndpoint);

            var form = new List<KeyValuePair<string, string>>
            {
                new("grant_type", "client_credentials")
            };

            if (!string.IsNullOrWhiteSpace(_options.ClientId))
            {
                form.Add(new KeyValuePair<string, string>("client_id", _options.ClientId));
            }

            if (!string.IsNullOrWhiteSpace(_options.Scope))
            {
                form.Add(new KeyValuePair<string, string>("scope", _options.Scope));
            }

            if (!string.IsNullOrWhiteSpace(_options.Audience))
            {
                form.Add(new KeyValuePair<string, string>("audience", _options.Audience));
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
            {
                Content = new FormUrlEncodedContent(form)
            };

            if (!string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                var basicValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicValue);
            }

            var client = _clientFactory.CreateClient("OidcClient");
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning("OIDC token request failed with status {StatusCode}.", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var accessToken = root.TryGetProperty("access_token", out var tokenProp) ? tokenProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                _logger.LogWarning("OIDC token response missing access_token.");
                return null;
            }

            var expiresIn = root.TryGetProperty("expires_in", out var expProp) && expProp.TryGetInt32(out var expVal)
                ? expVal
                : 300;
            var skew = Math.Max(0, _options.RefreshSkewSeconds);

            _accessToken = accessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - skew);

            _logger.LogInformation("Successfully obtained OIDC token, expires in {ExpiresIn} seconds", expiresIn);
            
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsTokenValid()
        => !string.IsNullOrWhiteSpace(_accessToken) && _expiresAt > DateTimeOffset.UtcNow;
}
