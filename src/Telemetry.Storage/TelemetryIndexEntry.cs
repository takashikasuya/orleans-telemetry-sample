namespace Telemetry.Storage;

public sealed record TelemetryIndexEntry(
    string TenantId,
    string DeviceId,
    DateTimeOffset BucketStart,
    DateTimeOffset MinOccurredAt,
    DateTimeOffset MaxOccurredAt,
    int RecordCount,
    IReadOnlyCollection<string> PointIds,
    string ParquetFile);
