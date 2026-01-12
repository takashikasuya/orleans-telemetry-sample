using System.Text.Json;

namespace Telemetry.E2E.Tests;

public sealed class TelemetryE2EReportOptions
{
    public string ReportPath { get; set; } = "reports";
    public int MaxApiLagMilliseconds { get; set; } = 5000;
    public int WaitTimeoutSeconds { get; set; } = 20;
}

public sealed class TelemetryE2EReport
{
    public string RunId { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; set; }
    public string Status { get; set; } = "Unknown";
    public string? Error { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string RdfSeedPath { get; set; } = string.Empty;
    public string ReportDirectory { get; set; } = string.Empty;
    public int MaxApiLagMilliseconds { get; set; }
    public TelemetryE2ESimulatorConfig Simulator { get; set; } = new();
    public TelemetryE2EEvent? SeedEvent { get; set; }
    public TelemetryE2EGraphBinding? Graph { get; set; }
    public TelemetryE2EApiCheck? Api { get; set; }
    public TelemetryE2EStorageCheck? Storage { get; set; }
}

public sealed class TelemetryE2ESimulatorConfig
{
    public string TenantId { get; set; } = string.Empty;
    public string BuildingName { get; set; } = string.Empty;
    public string SpaceId { get; set; } = string.Empty;
    public string DeviceIdPrefix { get; set; } = string.Empty;
    public int DeviceCount { get; set; }
    public int PointsPerDevice { get; set; }
    public int IntervalMilliseconds { get; set; }
}

public sealed class TelemetryE2EEvent
{
    public string TenantId { get; set; } = string.Empty;
    public string BuildingName { get; set; } = string.Empty;
    public string SpaceId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string PointId { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
    public string? ValueJson { get; set; }
}

public sealed class TelemetryE2EGraphBinding
{
    public string NodeId { get; set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; set; } = new();
}

public sealed class TelemetryE2EApiCheck
{
    public long PointLastSequence { get; set; }
    public DateTimeOffset PointUpdatedAt { get; set; }
    public string PointLatestValueJson { get; set; } = string.Empty;
    public DateTimeOffset PointReadAt { get; set; }
    public double PointLagMilliseconds { get; set; }
    public long DeviceLastSequence { get; set; }
    public DateTimeOffset DeviceUpdatedAt { get; set; }
    public string DevicePropertiesJson { get; set; } = string.Empty;
    public int TelemetryResultCount { get; set; }
    public string TelemetryFirstResultJson { get; set; } = string.Empty;
}

public sealed class TelemetryE2EStorageCheck
{
    public string StageFilePath { get; set; } = string.Empty;
    public string ParquetFilePath { get; set; } = string.Empty;
    public string IndexFilePath { get; set; } = string.Empty;
    public bool StageExists { get; set; }
    public bool ParquetExists { get; set; }
    public bool IndexExists { get; set; }
    public int CompactedBuckets { get; set; }
}

public static class TelemetryE2EReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task WriteAsync(TelemetryE2EReport report, string outputDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);

        var mdPath = Path.Combine(outputDir, $"{report.RunId}.md");
        var jsonPath = Path.Combine(outputDir, $"{report.RunId}.json");

        await File.WriteAllTextAsync(mdPath, ToMarkdown(report), ct);
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, JsonOptions), ct);
    }

    public static string ToMarkdown(TelemetryE2EReport report)
    {
        var lines = new List<string>
        {
            "# Telemetry E2E Report",
            $"RunId: {report.RunId}",
            $"Status: {report.Status}",
            $"StartedAt: {report.StartedAt:O}",
            $"CompletedAt: {report.CompletedAt:O}",
            $"TenantId: {report.TenantId}",
            $"RdfSeedPath: {report.RdfSeedPath}",
            $"ReportDirectory: {report.ReportDirectory}",
            $"MaxApiLagMilliseconds: {report.MaxApiLagMilliseconds}",
        };

        if (!string.IsNullOrWhiteSpace(report.Error))
        {
            lines.Add("");
            lines.Add("## Error");
            lines.Add(report.Error);
        }

        lines.Add("");
        lines.Add("## Simulator");
        lines.Add($"- TenantId: {report.Simulator.TenantId}");
        lines.Add($"- BuildingName: {report.Simulator.BuildingName}");
        lines.Add($"- SpaceId: {report.Simulator.SpaceId}");
        lines.Add($"- DeviceIdPrefix: {report.Simulator.DeviceIdPrefix}");
        lines.Add($"- DeviceCount: {report.Simulator.DeviceCount}");
        lines.Add($"- PointsPerDevice: {report.Simulator.PointsPerDevice}");
        lines.Add($"- IntervalMilliseconds: {report.Simulator.IntervalMilliseconds}");

        if (report.SeedEvent is not null)
        {
            lines.Add("");
            lines.Add("## Seed Event (E0)");
            lines.Add($"- TenantId: {report.SeedEvent.TenantId}");
            lines.Add($"- BuildingName: {report.SeedEvent.BuildingName}");
            lines.Add($"- SpaceId: {report.SeedEvent.SpaceId}");
            lines.Add($"- DeviceId: {report.SeedEvent.DeviceId}");
            lines.Add($"- PointId: {report.SeedEvent.PointId}");
            lines.Add($"- Sequence: {report.SeedEvent.Sequence}");
            lines.Add($"- OccurredAt: {report.SeedEvent.OccurredAt:O}");
            lines.Add($"- IngestedAt: {report.SeedEvent.IngestedAt:O}");
            lines.Add($"- ValueJson: {report.SeedEvent.ValueJson}");
        }

        if (report.Graph is not null)
        {
            lines.Add("");
            lines.Add("## Graph Binding");
            lines.Add($"- NodeId: {report.Graph.NodeId}");
            foreach (var kv in report.Graph.Attributes.OrderBy(kv => kv.Key))
            {
                lines.Add($"- {kv.Key}: {kv.Value}");
            }
        }

        if (report.Api is not null)
        {
            lines.Add("");
            lines.Add("## API Checks");
            lines.Add($"- PointLastSequence: {report.Api.PointLastSequence}");
            lines.Add($"- PointUpdatedAt: {report.Api.PointUpdatedAt:O}");
            lines.Add($"- PointLatestValueJson: {report.Api.PointLatestValueJson}");
            lines.Add($"- PointReadAt: {report.Api.PointReadAt:O}");
            lines.Add($"- PointLagMilliseconds: {report.Api.PointLagMilliseconds:0.0}");
            lines.Add($"- DeviceLastSequence: {report.Api.DeviceLastSequence}");
            lines.Add($"- DeviceUpdatedAt: {report.Api.DeviceUpdatedAt:O}");
            lines.Add($"- DevicePropertiesJson: {report.Api.DevicePropertiesJson}");
            lines.Add($"- TelemetryResultCount: {report.Api.TelemetryResultCount}");
            lines.Add($"- TelemetryFirstResultJson: {report.Api.TelemetryFirstResultJson}");
        }

        if (report.Storage is not null)
        {
            lines.Add("");
            lines.Add("## Storage");
            lines.Add($"- StageFilePath: {report.Storage.StageFilePath}");
            lines.Add($"- StageExists: {report.Storage.StageExists}");
            lines.Add($"- ParquetFilePath: {report.Storage.ParquetFilePath}");
            lines.Add($"- ParquetExists: {report.Storage.ParquetExists}");
            lines.Add($"- IndexFilePath: {report.Storage.IndexFilePath}");
            lines.Add($"- IndexExists: {report.Storage.IndexExists}");
            lines.Add($"- CompactedBuckets: {report.Storage.CompactedBuckets}");
        }

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }
}
