using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiGateway.Client;

public sealed class ApiClientReport
{
    public string RunId { get; set; } = string.Empty;
    public string Status { get; set; } = "Failed";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string OidcAuthority { get; set; } = string.Empty;
    public string? TokenEndpoint { get; set; }
    public string? TokenType { get; set; }
    public int? TokenExpiresIn { get; set; }
    public string? TenantId { get; set; }
    public RegistrySnapshot Registry { get; set; } = new();
    public GraphSnapshot Graph { get; set; } = new();
    public TelemetrySnapshot Telemetry { get; set; } = new();
    public List<ApiCallLog> ApiCalls { get; set; } = new();
    public string? Error { get; set; }
}

public sealed class RegistrySnapshot
{
    public int? SitesCount { get; set; }
    public int? BuildingsCount { get; set; }
    public int? DevicesCount { get; set; }
    public int? PointsCount { get; set; }
    public string? SelectedSiteNodeId { get; set; }
    public string? SelectedBuildingNodeId { get; set; }
    public string? SelectedPointNodeId { get; set; }
}

public sealed class GraphSnapshot
{
    public string? NodeId { get; set; }
    public int? OutgoingEdgeCount { get; set; }
    public int? IncomingEdgeCount { get; set; }
    public int? TraversedNodeCount { get; set; }
    public Dictionary<string, string>? NodeAttributes { get; set; }
    public List<GraphEdgeSummary> Relationships { get; set; } = new();
}

public sealed class GraphEdgeSummary
{
    public string Predicate { get; set; } = string.Empty;
    public string TargetNodeId { get; set; } = string.Empty;
}

public sealed class TelemetrySnapshot
{
    public string? DeviceId { get; set; }
    public string? PointId { get; set; }
    public long? PointLastSequence { get; set; }
    public DateTimeOffset? PointUpdatedAt { get; set; }
    public string? PointLatestValueJson { get; set; }
    public string? PointSnapshotJson { get; set; }
    public long? DeviceLastSequence { get; set; }
    public DateTimeOffset? DeviceUpdatedAt { get; set; }
    public string? DevicePropertiesJson { get; set; }
    public string? DeviceSnapshotJson { get; set; }
    public int? HistoryResultCount { get; set; }
    public string? HistoryMode { get; set; }
    public string? HistoryExportUrl { get; set; }
    public string? HistoryFirstResultJson { get; set; }
    public List<string> HistorySamplesJson { get; set; } = new();
}

public sealed class ApiCallLog
{
    public string Name { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public int ResponseBytes { get; set; }
    public string? ResponsePreview { get; set; }
}

public static class ApiClientReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task WriteAsync(ApiClientReport report, string outputDir, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDir);
        var mdPath = Path.Combine(outputDir, $"{report.RunId}.md");
        var jsonPath = Path.Combine(outputDir, $"{report.RunId}.json");

        await File.WriteAllTextAsync(mdPath, ToMarkdown(report), Encoding.UTF8, ct);
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, JsonOptions), Encoding.UTF8, ct);
    }

    public static string ToMarkdown(ApiClientReport report)
    {
        var lines = new List<string>
        {
            "# ApiGateway Client Report",
            string.Empty,
            $"RunId: {report.RunId}",
            $"Status: {report.Status}",
            $"StartedAt: {report.StartedAt:O}",
            $"CompletedAt: {report.CompletedAt:O}",
            $"ApiBaseUrl: {report.ApiBaseUrl}",
            $"OidcAuthority: {report.OidcAuthority}",
            $"TokenEndpoint: {report.TokenEndpoint}",
            string.Empty,
            "## Registry",
            $"- SitesCount: {report.Registry.SitesCount}",
            $"- BuildingsCount: {report.Registry.BuildingsCount}",
            $"- DevicesCount: {report.Registry.DevicesCount}",
            $"- PointsCount: {report.Registry.PointsCount}",
            $"- SelectedSiteNodeId: {report.Registry.SelectedSiteNodeId}",
            $"- SelectedBuildingNodeId: {report.Registry.SelectedBuildingNodeId}",
            $"- SelectedPointNodeId: {report.Registry.SelectedPointNodeId}",
            string.Empty,
            "## Graph",
            $"- NodeId: {report.Graph.NodeId}",
            $"- TraversedNodeCount: {report.Graph.TraversedNodeCount}",
            $"- OutgoingEdgeCount: {report.Graph.OutgoingEdgeCount}",
            $"- IncomingEdgeCount: {report.Graph.IncomingEdgeCount}",
            string.Empty,
            "## Telemetry",
            $"- DeviceId: {report.Telemetry.DeviceId}",
            $"- PointId: {report.Telemetry.PointId}",
            $"- PointLastSequence: {report.Telemetry.PointLastSequence}",
            $"- PointUpdatedAt: {report.Telemetry.PointUpdatedAt:O}",
            $"- DeviceLastSequence: {report.Telemetry.DeviceLastSequence}",
            $"- DeviceUpdatedAt: {report.Telemetry.DeviceUpdatedAt:O}",
            $"- HistoryResultCount: {report.Telemetry.HistoryResultCount}",
            $"- HistoryMode: {report.Telemetry.HistoryMode}",
            $"- HistoryExportUrl: {report.Telemetry.HistoryExportUrl}",
            string.Empty,
            "### Point Snapshot (Raw JSON)",
            string.Empty,
            "### Device Snapshot (Raw JSON)",
            string.Empty,
            "### Telemetry History Samples (Raw JSON)",
            string.Empty,
            "## Calls"
        };

        InsertSectionPayload(lines, "### Point Snapshot (Raw JSON)", report.Telemetry.PointSnapshotJson);
        InsertSectionPayload(lines, "### Device Snapshot (Raw JSON)", report.Telemetry.DeviceSnapshotJson);
        InsertSectionSamples(lines, "### Telemetry History Samples (Raw JSON)", report.Telemetry.HistorySamplesJson);

        foreach (var call in report.ApiCalls)
        {
            lines.Add($"- {call.Method} {call.Url} ({call.StatusCode}, {call.ElapsedMilliseconds}ms, {call.ResponseBytes} bytes)");
        }

        if (!string.IsNullOrWhiteSpace(report.Error))
        {
            lines.Add(string.Empty);
            lines.Add("## Error");
            lines.Add(report.Error);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void InsertSectionPayload(List<string> lines, string header, string? payload)
    {
        var index = lines.IndexOf(header);
        if (index < 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            lines.Insert(index + 1, "(none)");
            return;
        }

        lines.Insert(index + 1, "```json");
        lines.Insert(index + 2, payload);
        lines.Insert(index + 3, "```");
    }

    private static void InsertSectionSamples(List<string> lines, string header, IReadOnlyList<string> samples)
    {
        var index = lines.IndexOf(header);
        if (index < 0)
        {
            return;
        }

        if (samples.Count == 0)
        {
            lines.Insert(index + 1, "(none)");
            return;
        }

        var offset = 1;
        foreach (var sample in samples)
        {
            lines.Insert(index + offset++, "```json");
            lines.Insert(index + offset++, sample);
            lines.Insert(index + offset++, "```");
        }
    }
}
