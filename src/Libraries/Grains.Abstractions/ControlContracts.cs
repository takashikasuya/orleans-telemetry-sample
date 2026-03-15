using Orleans;

namespace Grains.Abstractions;

/// <summary>
/// Represents the status of a control request.
/// </summary>
public enum ControlRequestStatus
{
    /// <summary>
    /// The request is pending processing.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The request has been accepted.
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// The request has been applied.
    /// </summary>
    Applied = 2,

    /// <summary>
    /// The request was rejected.
    /// </summary>
    Rejected = 3,

    /// <summary>
    /// The request failed.
    /// </summary>
    Failed = 4,

    /// <summary>
    /// The request timed out.
    /// </summary>
    Timeout = 5
}

/// <summary>
/// Represents a control command request for a point.
/// </summary>
[GenerateSerializer]
public sealed class PointControlRequest
{
    /// <summary>
    /// Gets or sets the unique command identifier.
    /// </summary>
    [Id(0)] public string CommandId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    [Id(1)] public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the building name.
    /// </summary>
    [Id(2)] public string BuildingName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the space identifier.
    /// </summary>
    [Id(3)] public string SpaceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the device identifier.
    /// </summary>
    [Id(4)] public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the point identifier.
    /// </summary>
    [Id(5)] public string PointId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the desired target value.
    /// </summary>
    [Id(6)] public object? DesiredValue { get; set; }

    /// <summary>
    /// Gets or sets the request timestamp.
    /// </summary>
    [Id(7)] public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets additional metadata.
    /// </summary>
    [Id(8)] public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Represents the latest state of a submitted point control command.
/// </summary>
/// <param name="CommandId">Unique command identifier.</param>
/// <param name="Status">Current request status.</param>
/// <param name="DesiredValue">Requested value.</param>
/// <param name="RequestedAt">Request timestamp.</param>
/// <param name="AcceptedAt">Acceptance timestamp.</param>
/// <param name="AppliedAt">Apply timestamp.</param>
/// <param name="ConnectorName">Connector that handled the request.</param>
/// <param name="CorrelationId">Connector-side correlation identifier.</param>
/// <param name="LastError">Last error message when failed.</param>
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

/// <summary>
/// Grain contract for point control command tracking.
/// </summary>
public interface IPointControlGrain : IGrainWithStringKey
{
    /// <summary>
    /// Submits a control request.
    /// </summary>
    /// <param name="request">Control request payload.</param>
    /// <returns>Current snapshot for the submitted command.</returns>
    Task<PointControlSnapshot> SubmitAsync(PointControlRequest request);

    /// <summary>
    /// Gets control request snapshot by command identifier.
    /// </summary>
    /// <param name="commandId">Command identifier.</param>
    /// <returns>Snapshot when found; otherwise null.</returns>
    Task<PointControlSnapshot?> GetAsync(string commandId);
}
