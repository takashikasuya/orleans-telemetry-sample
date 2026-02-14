namespace Telemetry.Ingest.RabbitMq;

public sealed class RabbitMqIngestOptions
{
    public string? HostName { get; set; }

    public int? Port { get; set; }

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string? QueueName { get; set; } = "telemetry";

    public ushort PrefetchCount { get; set; } = 100;
}
