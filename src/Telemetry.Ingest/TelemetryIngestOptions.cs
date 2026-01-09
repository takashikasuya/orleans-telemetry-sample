namespace Telemetry.Ingest;

public sealed class TelemetryIngestOptions
{
    public string[]? Enabled { get; set; }

    public int ChannelCapacity { get; set; } = 10000;

    public int BatchSize { get; set; } = 100;
}
