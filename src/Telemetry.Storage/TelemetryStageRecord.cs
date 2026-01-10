using System.Text.Json;
using Telemetry.Ingest;

namespace Telemetry.Storage;

public sealed record TelemetryStageRecord(
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
    string? ValueJson,
    string? PayloadJson,
    IReadOnlyDictionary<string, string>? Tags)
{
    public static TelemetryStageRecord FromEnvelope(TelemetryEventEnvelope envelope)
    {
        var valueJson = envelope.Value is null ? null : JsonSerializer.Serialize(envelope.Value);
        var payloadJson = envelope.Payload?.RootElement.GetRawText();
        return new TelemetryStageRecord(
            envelope.TenantId,
            envelope.BuildingName,
            envelope.SpaceId,
            envelope.DeviceId,
            envelope.PointId,
            envelope.Sequence,
            envelope.OccurredAt,
            envelope.IngestedAt,
            envelope.EventType,
            envelope.Severity,
            valueJson,
            payloadJson,
            envelope.Tags);
    }
}
