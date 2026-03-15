using Microsoft.Extensions.Logging;
using Telemetry.Ingest;

namespace ApiGateway.Services;

public sealed class ControlEgressDispatcher
{
    private readonly IReadOnlyDictionary<string, IControlEgressConnector> _connectors;
    private readonly ILogger<ControlEgressDispatcher> _logger;

    public ControlEgressDispatcher(
        IEnumerable<IControlEgressConnector> connectors,
        ILogger<ControlEgressDispatcher> logger)
    {
        var dict = new Dictionary<string, IControlEgressConnector>(StringComparer.OrdinalIgnoreCase);
        foreach (var connector in connectors)
        {
            dict[connector.Name] = connector;
        }

        _connectors = dict;
        _logger = logger;
    }

    public async Task<ControlEgressResult?> SendAsync(
        string connectorName,
        ControlEgressRequest request,
        CancellationToken ct)
    {
        if (!_connectors.TryGetValue(connectorName, out var connector))
        {
            _logger.LogWarning(
                "No egress connector registered for {ConnectorName}; command {CommandId} not delivered",
                connectorName,
                request.CommandId);
            return null;
        }

        try
        {
            var result = await connector.SendAsync(request, ct);
            if (result.Accepted)
            {
                _logger.LogInformation(
                    "Control command {CommandId} delivered via {ConnectorName} (correlationId={CorrelationId})",
                    request.CommandId,
                    connectorName,
                    result.CorrelationId);
            }
            else
            {
                _logger.LogWarning(
                    "Control command {CommandId} rejected by {ConnectorName}: {Error}",
                    request.CommandId,
                    connectorName,
                    result.Error);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Control egress failed for connector {ConnectorName}, command {CommandId}",
                connectorName,
                request.CommandId);
            return new ControlEgressResult
            {
                CommandId = request.CommandId,
                ConnectorName = connectorName,
                Accepted = false,
                Error = ex.Message,
                ConfirmMode = connector.ConfirmMode
            };
        }
    }
}
