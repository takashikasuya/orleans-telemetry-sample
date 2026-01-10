using Grains.Abstractions;

namespace Telemetry.Ingest;

public static class TelemetryEventEnvelopeFactory
{
    public static TelemetryEventEnvelope FromTelemetryPoint(
        TelemetryPointMsg msg,
        DateTimeOffset ingestedAt)
    {
        return new TelemetryEventEnvelope(
            msg.TenantId,
            msg.BuildingName,
            msg.SpaceId,
            msg.DeviceId,
            msg.PointId,
            msg.Sequence,
            msg.Timestamp,
            ingestedAt,
            TelemetryEventType.Telemetry,
            null,
            msg.Value,
            null,
            null);
    }
}
