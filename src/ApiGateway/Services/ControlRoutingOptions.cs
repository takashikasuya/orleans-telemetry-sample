using System.Collections.Generic;

namespace ApiGateway.Services;

public sealed class ControlRoutingOptions
{
    public string? DefaultConnector { get; set; }

    public List<ControlConnectorGatewayMappingOptions> ConnectorGatewayMappings { get; set; } = new();

    public List<ControlRoutingRuleOptions> Rules { get; set; } = new();
}

public sealed class ControlConnectorGatewayMappingOptions
{
    public string Connector { get; set; } = string.Empty;

    public List<string> GatewayIds { get; set; } = new();
}

public sealed class ControlRoutingRuleOptions
{
    public string Name { get; set; } = string.Empty;

    public string Connector { get; set; } = string.Empty;

    public string? GatewayPattern { get; set; }

    public string? DevicePattern { get; set; }

    public string? PointPattern { get; set; }
}

public sealed record ControlRouteContext(
    string DeviceId,
    string PointId,
    string? GatewayId);
