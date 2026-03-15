using System.Text.Json;
using Telemetry.Storage;

namespace ApiGateway.Telemetry;

/// <summary>
/// Determines whether telemetry query results should be returned inline or as export URL.
/// </summary>
public static class TelemetryInlineResponsePolicy
{
    /// <summary>
    /// Evaluates inline response eligibility based on estimated payload size.
    /// </summary>
    /// <param name="results">Query results.</param>
    /// <param name="options">Export options including inline size threshold.</param>
    /// <returns>True when inline response is preferred.</returns>
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
