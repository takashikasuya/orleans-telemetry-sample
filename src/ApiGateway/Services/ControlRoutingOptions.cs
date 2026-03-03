using System.Collections.Generic;

namespace ApiGateway.Services;

/// <summary>
/// Configuration options for control connector routing.
/// </summary>
public sealed class ControlRoutingOptions
{
    /// <summary>
    /// Gets or sets the default connector name used when no mapping or rule matches.
    /// </summary>
    public string? DefaultConnector { get; set; }

    /// <summary>
    /// Gets or sets connector-to-gateway mapping definitions.
    /// </summary>
    public List<ControlConnectorGatewayMappingOptions> ConnectorGatewayMappings { get; set; } = new();

    /// <summary>
    /// Gets or sets rule-based routing definitions.
    /// </summary>
    public List<ControlRoutingRuleOptions> Rules { get; set; } = new();
}

/// <summary>
/// Defines gateway identifiers handled by a connector.
/// </summary>
public sealed class ControlConnectorGatewayMappingOptions
{
    /// <summary>
    /// Gets or sets the connector name.
    /// </summary>
    public string Connector { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets gateway identifiers associated with the connector.
    /// </summary>
    public List<string> GatewayIds { get; set; } = new();
}

/// <summary>
/// Defines a pattern-based control routing rule.
/// </summary>
public sealed class ControlRoutingRuleOptions
{
    /// <summary>
    /// Gets or sets rule name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets connector name selected by this rule.
    /// </summary>
    public string Connector { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets gateway identifier regex pattern.
    /// </summary>
    public string? GatewayPattern { get; set; }

    /// <summary>
    /// Gets or sets device identifier regex pattern.
    /// </summary>
    public string? DevicePattern { get; set; }

    /// <summary>
    /// Gets or sets point identifier regex pattern.
    /// </summary>
    public string? PointPattern { get; set; }
}

/// <summary>
/// Represents normalized values used to resolve a routing target.
/// </summary>
/// <param name="DeviceId">Device identifier.</param>
/// <param name="PointId">Point identifier.</param>
/// <param name="GatewayId">Optional gateway identifier.</param>
public sealed record ControlRouteContext(
    string DeviceId,
    string PointId,
    string? GatewayId);
