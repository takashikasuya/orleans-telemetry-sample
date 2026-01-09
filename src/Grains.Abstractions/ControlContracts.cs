using Orleans;

namespace Grains.Abstractions;

public enum ControlRequestStatus
{
    Pending = 0,
    Accepted = 1,
    Applied = 2,
    Rejected = 3,
    Failed = 4,
    Timeout = 5
}

[GenerateSerializer]
public sealed class PointControlRequest
{
    [Id(0)] public string CommandId { get; set; } = string.Empty;
    [Id(1)] public string TenantId { get; set; } = string.Empty;
    [Id(2)] public string BuildingName { get; set; } = string.Empty;
    [Id(3)] public string SpaceId { get; set; } = string.Empty;
    [Id(4)] public string DeviceId { get; set; } = string.Empty;
    [Id(5)] public string PointId { get; set; } = string.Empty;
    [Id(6)] public object? DesiredValue { get; set; }
    [Id(7)] public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    [Id(8)] public Dictionary<string, string> Metadata { get; set; } = new();
}

[GenerateSerializer]
public sealed record PointControlSnapshot(
    string CommandId,
    ControlRequestStatus Status,
    object? DesiredValue,
    DateTimeOffset RequestedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? AppliedAt,
    string? ConnectorName,
    string? CorrelationId,
    string? LastError);

public interface IPointControlGrain : IGrainWithStringKey
{
    Task<PointControlSnapshot> SubmitAsync(PointControlRequest request);
    Task<PointControlSnapshot?> GetAsync(string commandId);
}
