namespace Telemetry.Ingest.Kafka;

public sealed class KafkaIngestOptions
{
    public string? BootstrapServers { get; set; }

    public string? GroupId { get; set; }

    public string? Topic { get; set; } = "telemetry";

    public bool EnableAutoCommit { get; set; }

    public string AutoOffsetReset { get; set; } = "Latest";

    public int? SessionTimeoutMs { get; set; }

    public int? MaxPollIntervalMs { get; set; }
}
