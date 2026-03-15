namespace Telemetry.Ingest;

public sealed class TelemetryIngestOptions
{
    public string[]? Enabled { get; set; }

    public TelemetryIngestEventSinkOptions EventSinks { get; set; } = new();

    public int ChannelCapacity { get; set; } = 10000;

    public int BatchSize { get; set; } = 100;
}

public sealed class TelemetryIngestEventSinkOptions
{
    public string[]? Enabled { get; set; }
}
