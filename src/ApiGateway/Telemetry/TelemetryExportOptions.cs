namespace ApiGateway.Telemetry;

public sealed class TelemetryExportOptions
{
    public string ExportRoot { get; set; } = "storage/exports";
    public int DefaultTtlMinutes { get; set; } = 60;
    public int CleanupIntervalSeconds { get; set; } = 300;
    public int MaxInlineBytes { get; set; } = 5 * 1024 * 1024;
}
