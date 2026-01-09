namespace ApiGateway.Contracts;

public sealed record PointControlRequest(
    string CommandId,
    string BuildingName,
    string SpaceId,
    string DeviceId,
    string PointId,
    object? DesiredValue,
    Dictionary<string, string>? Metadata);

public sealed record PointControlResponse(
    string CommandId,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? AppliedAt,
    string? ConnectorName,
    string? CorrelationId,
    string? LastError);
