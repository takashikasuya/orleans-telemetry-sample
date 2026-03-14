using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdminGateway.E2E.Tests;

#pragma warning disable CS0618
internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    private const string TenantClaimKey = "tenant";
    private const string TenantPrefix = "tenant=";

    public static bool EnforceAuthorizationHeader { get; set; } = false;
    public static bool ForceFailure { get; set; }
    public static string DefaultTenantId { get; set; } = "t1";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }
#pragma warning restore CS0618

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (ForceFailure)
        {
            return Task.FromResult(AuthenticateResult.Fail("forced failure"));
        }

        Request.Headers.TryGetValue("Authorization", out var headerValues);
        var headerValue = headerValues.ToString();

        if (EnforceAuthorizationHeader && string.IsNullOrWhiteSpace(headerValue))
        {
            return Task.FromResult(AuthenticateResult.Fail("missing authorization header"));
        }

        if (EnforceAuthorizationHeader && !headerValue.StartsWith($"{SchemeName} ", System.StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("invalid scheme"));
        }

        var tenant = ExtractTenant(headerValue) ?? DefaultTenantId;
        var claims = new List<Claim>
        {
            new(TenantClaimKey, tenant),
            new(ClaimTypes.Name, "test-user")
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static string? ExtractTenant(string headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        var lower = headerValue.ToLowerInvariant();
        var tenantIndex = lower.IndexOf(TenantPrefix, System.StringComparison.Ordinal);
        if (tenantIndex < 0)
        {
            return null;
        }

        var start = tenantIndex + TenantPrefix.Length;
        var end = headerValue.IndexOf(';', start);
        return end >= 0
            ? headerValue[start..end]
            : headerValue[start..];
    }

    public static void Reset()
    {
        EnforceAuthorizationHeader = false;
        ForceFailure = false;
        DefaultTenantId = "t1";
    }
}
