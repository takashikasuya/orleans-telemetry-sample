namespace Telemetry.Ingest;

public interface ITelemetryEventSink
{
    string Name { get; }

    Task WriteAsync(TelemetryEventEnvelope envelope, CancellationToken ct);

    Task WriteBatchAsync(IReadOnlyList<TelemetryEventEnvelope> batch, CancellationToken ct);
}
