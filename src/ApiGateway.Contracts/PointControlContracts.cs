using System;
using System.Collections.Generic;

namespace ApiGateway.Contracts;

/// <summary>
/// Represents an API request to control a point value.
/// </summary>
/// <param name="CommandId">Unique command identifier.</param>
/// <param name="BuildingName">Building name associated with the point.</param>
/// <param name="SpaceId">Space identifier associated with the point.</param>
/// <param name="DeviceId">Device identifier.</param>
/// <param name="PointId">Point identifier.</param>
/// <param name="DesiredValue">Desired target value.</param>
/// <param name="Metadata">Optional command metadata.</param>
public sealed record PointControlRequest(
    string CommandId,
    string BuildingName,
    string SpaceId,
    string DeviceId,
    string PointId,
    object? DesiredValue,
    Dictionary<string, string>? Metadata);

/// <summary>
/// Represents an API response for a point control command.
/// </summary>
/// <param name="CommandId">Unique command identifier.</param>
/// <param name="Status">Current command status.</param>
/// <param name="RequestedAt">Request timestamp.</param>
/// <param name="AcceptedAt">Acceptance timestamp.</param>
/// <param name="AppliedAt">Apply timestamp.</param>
/// <param name="ConnectorName">Connector selected for delivery.</param>
/// <param name="CorrelationId">Connector-side correlation identifier.</param>
/// <param name="LastError">Last error message when failed.</param>
public sealed record PointControlResponse(
    string CommandId,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? AppliedAt,
    string? ConnectorName,
    string? CorrelationId,
    string? LastError);
