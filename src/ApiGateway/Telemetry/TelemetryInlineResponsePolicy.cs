using System.Text.Json;
using Telemetry.Storage;

namespace ApiGateway.Telemetry;

public static class TelemetryInlineResponsePolicy
{
    public static bool ShouldReturnInline(IReadOnlyList<TelemetryQueryResult> results, TelemetryExportOptions options)
    {
        if (results.Count == 0)
        {
            return true;
        }

        var maxInlineBytes = Math.Max(1, options.MaxInlineBytes);
        return EstimateInlineBytes(results) <= maxInlineBytes;
    }

    internal static int EstimateInlineBytes(IReadOnlyList<TelemetryQueryResult> results)
    {
        var response = TelemetryQueryResponse.Inline(results);
        return JsonSerializer.SerializeToUtf8Bytes(response).Length;
    }
}
