namespace Telemetry.Ingest;

public enum ControlConfirmMode
{
    AckOnly = 0,
    TelemetryConfirm = 1,
    ReadBackConfirm = 2
}

public sealed class ControlEgressRequest
{
    public string CommandId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string BuildingName { get; set; } = string.Empty;
    public string SpaceId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string PointId { get; set; } = string.Empty;
    public object? DesiredValue { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public sealed class ControlEgressResult
{
    public string CommandId { get; set; } = string.Empty;
    public string ConnectorName { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public string? CorrelationId { get; set; }
    public string? Error { get; set; }
    public ControlConfirmMode ConfirmMode { get; set; }
}

public interface IControlEgressConnector
{
    string Name { get; }
    ControlConfirmMode ConfirmMode { get; }
    Task<ControlEgressResult> SendAsync(ControlEgressRequest request, CancellationToken ct);
}
