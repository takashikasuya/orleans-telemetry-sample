using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Infrastructure;

internal static class TenantResolver
{
    private const string TenantClaimType = "tenant";
    private const string DefaultTenant = "t1";

    public static string ResolveTenant(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return DefaultTenant;
        }

        return ResolveTenant(httpContext.User);
    }

    public static string ResolveTenant(ClaimsPrincipal principal)
    {
        var tenant = principal.FindFirst(TenantClaimType)?.Value;
        return string.IsNullOrWhiteSpace(tenant) ? DefaultTenant : tenant;
    }
}
