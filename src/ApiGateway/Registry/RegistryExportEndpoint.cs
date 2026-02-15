using ApiGateway.Infrastructure;
using ApiGateway.Services;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.Registry;

internal static class RegistryExportEndpoint
{
    public static async Task<IResult> HandleOpenExportAsync(
        string exportId,
        RegistryExportService exports,
        HttpContext http,
        DateTimeOffset now)
    {
        var tenant = TenantResolver.ResolveTenant(http);
        var result = await exports.TryOpenExportAsync(exportId, tenant, now, http.RequestAborted);

        return result.Status switch
        {
            RegistryExportOpenStatus.NotFound => Results.NotFound(),
            RegistryExportOpenStatus.Expired => Results.StatusCode(StatusCodes.Status410Gone),
            RegistryExportOpenStatus.Ready => BuildFileResult(result),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static IResult BuildFileResult(RegistryExportOpenResult result)
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
            $"registry_{metadata.ExportId}.jsonl");
    }
}
