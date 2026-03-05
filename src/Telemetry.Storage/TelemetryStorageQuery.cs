namespace Telemetry.Storage;

public sealed record TelemetryQueryRequest(
    string TenantId,
    string DeviceId,
    DateTimeOffset From,
    DateTimeOffset To,
    string? PointId,
    int? Limit);

public sealed record TelemetryQueryResult(
    string TenantId,
    string DeviceId,
    string PointId,
    DateTimeOffset OccurredAt,
    long Sequence,
    string? ValueJson,
    string? PayloadJson,
    IReadOnlyDictionary<string, string>? Tags,
    DateTimeOffset IngestedAt = default,
    int EventType = 0,
    int? Severity = null);

public interface ITelemetryStorageQuery
{
    Task<IReadOnlyList<TelemetryQueryResult>> QueryAsync(TelemetryQueryRequest request, CancellationToken ct);
}
