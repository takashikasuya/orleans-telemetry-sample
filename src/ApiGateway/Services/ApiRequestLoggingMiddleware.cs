using System.Diagnostics;
using ApiGateway.Infrastructure;

namespace ApiGateway.Services;

public sealed class ApiRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public ApiRequestLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ApiRequestLogDispatcher dispatcher)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        Exception? requestException = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            requestException = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            var statusCode = requestException is null ? context.Response.StatusCode : StatusCodes.Status500InternalServerError;
            var tenant = TenantResolver.ResolveTenant(context);
            var user = context.User?.Identity?.Name;
            var traceId = Activity.Current?.TraceId.ToString();

            await dispatcher.WriteHttpRequestLogAsync(
                tenant,
                context.Request.Method,
                context.Request.Path.ToString(),
                statusCode,
                stopwatch.Elapsed.TotalMilliseconds,
                traceId,
                context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
                user,
                context.RequestAborted);
        }
    }
}
