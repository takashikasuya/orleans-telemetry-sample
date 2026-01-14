using ApiGateway.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Telemetry;

internal static class TelemetryExportEndpoint
{
    public static async Task<IResult> HandleOpenExportAsync(
        string exportId,
        TelemetryExportService exports,
        HttpContext http,
        DateTimeOffset now)
    {
        var tenant = TenantResolver.ResolveTenant(http);
        var result = await exports.TryOpenExportAsync(exportId, tenant, now, http.RequestAborted);
        return result.Status switch
        {
            TelemetryExportOpenStatus.NotFound => Results.NotFound(),
            TelemetryExportOpenStatus.Expired => Results.StatusCode(StatusCodes.Status410Gone),
            TelemetryExportOpenStatus.Ready => BuildFileResult(result),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static IResult BuildFileResult(TelemetryExportOpenResult result)
    {
        var metadata = result.Metadata;
        var stream = result.Stream;
        if (metadata is null || stream is null)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Results.File(
            stream,
            metadata.ContentType,
            $"telemetry_{metadata.ExportId}.jsonl");
    }
}
