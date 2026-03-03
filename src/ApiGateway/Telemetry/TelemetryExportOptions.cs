namespace ApiGateway.Telemetry;

/// <summary>
/// Configuration options for telemetry export persistence and cleanup.
/// </summary>
public sealed class TelemetryExportOptions
{
    /// <summary>
    /// Gets or sets the export root directory.
    /// </summary>
    public string ExportRoot { get; set; } = "storage/exports";

    /// <summary>
    /// Gets or sets the default export time-to-live in minutes.
    /// </summary>
    public int DefaultTtlMinutes { get; set; } = 60;

    /// <summary>
    /// Gets or sets cleanup interval in seconds.
    /// </summary>
    public int CleanupIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets maximum inline response size in bytes.
    /// </summary>
    public int MaxInlineBytes { get; set; } = 5 * 1024 * 1024;
}
