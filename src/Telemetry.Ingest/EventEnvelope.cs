using System.Text.Json;

namespace Telemetry.Ingest;

public enum TelemetryEventType
{
    Telemetry = 0,
    Log = 1,
    Control = 2
}

public enum TelemetryLogSeverity
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}

public sealed record TelemetryEventEnvelope(
    string TenantId,
    string BuildingName,
    string SpaceId,
    string DeviceId,
    string PointId,
    long Sequence,
    DateTimeOffset OccurredAt,
    DateTimeOffset IngestedAt,
    TelemetryEventType EventType,
    TelemetryLogSeverity? Severity,
    object? Value,
    JsonDocument? Payload,
    IReadOnlyDictionary<string, string>? Tags);
