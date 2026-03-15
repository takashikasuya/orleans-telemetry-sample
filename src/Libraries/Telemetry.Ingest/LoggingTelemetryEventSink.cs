using Microsoft.Extensions.Logging;

namespace Telemetry.Ingest;

public sealed class LoggingTelemetryEventSink : ITelemetryEventSink
{
    private readonly ILogger<LoggingTelemetryEventSink> _logger;

    public LoggingTelemetryEventSink(ILogger<LoggingTelemetryEventSink> logger)
    {
        _logger = logger;
    }

    public string Name => "Logging";

    public Task WriteAsync(TelemetryEventEnvelope envelope, CancellationToken ct)
    {
        return WriteBatchAsync(new[] { envelope }, ct);
    }

    public Task WriteBatchAsync(IReadOnlyList<TelemetryEventEnvelope> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return Task.CompletedTask;
        }

        var first = batch[0];
        _logger.LogInformation(
            "Telemetry event batch {Count} for tenant {TenantId} device {DeviceId}.",
            batch.Count,
            first.TenantId,
            first.DeviceId);
        return Task.CompletedTask;
    }
}
