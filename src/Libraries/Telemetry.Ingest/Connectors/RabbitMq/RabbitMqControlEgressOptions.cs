namespace Telemetry.Ingest.RabbitMq;

public sealed class RabbitMqControlEgressOptions
{
    public string? HostName { get; set; }

    public int? Port { get; set; }

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string? QueueName { get; set; } = "telemetry-control";
}
