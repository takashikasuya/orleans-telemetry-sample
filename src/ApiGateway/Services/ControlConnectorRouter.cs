using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ApiGateway.Services;

public sealed class ControlConnectorRouter
{
    private readonly string? _defaultConnector;
    private readonly IReadOnlyDictionary<string, string> _gatewayConnectorMap;
    private readonly IReadOnlyList<CompiledRule> _rules;

    public ControlConnectorRouter(IOptions<ControlRoutingOptions> options)
    {
        var value = options.Value ?? new ControlRoutingOptions();
        _defaultConnector = Normalize(value.DefaultConnector);

        _gatewayConnectorMap = value.ConnectorGatewayMappings
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.Connector))
            .SelectMany(mapping => mapping.GatewayIds.Select(gatewayId => new
            {
                GatewayId = gatewayId.Trim(),
                Connector = mapping.Connector.Trim()
            }))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.GatewayId))
            .ToDictionary(entry => entry.GatewayId, entry => entry.Connector, StringComparer.OrdinalIgnoreCase);

        _rules = value.Rules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Connector))
            .Select(rule => new CompiledRule(
                string.IsNullOrWhiteSpace(rule.Name) ? "unnamed" : rule.Name,
                rule.Connector.Trim(),
                Compile(rule.GatewayPattern),
                Compile(rule.DevicePattern),
                Compile(rule.PointPattern)))
            .ToArray();
    }

    public string? ResolveConnector(ControlRouteContext context, out string? matchedRule)
    {
        if (!string.IsNullOrWhiteSpace(context.GatewayId)
            && _gatewayConnectorMap.TryGetValue(context.GatewayId, out var mappedConnector))
        {
            matchedRule = "gateway-map";
            return mappedConnector;
        }

        foreach (var rule in _rules)
        {
            if (!IsMatch(rule.GatewayRegex, context.GatewayId))
            {
                continue;
            }

            if (!IsMatch(rule.DeviceRegex, context.DeviceId))
            {
                continue;
            }

            if (!IsMatch(rule.PointRegex, context.PointId))
            {
                continue;
            }

            matchedRule = rule.Name;
            return rule.Connector;
        }

        matchedRule = null;
        return _defaultConnector;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Regex? Compile(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static bool IsMatch(Regex? regex, string? value)
    {
        if (regex is null)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(value) && regex.IsMatch(value);
    }

    private sealed record CompiledRule(
        string Name,
        string Connector,
        Regex? GatewayRegex,
        Regex? DeviceRegex,
        Regex? PointRegex);
}
