using Grains.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;
using Telemetry.Ingest;

namespace SiloHost;

public sealed class GraphRegisteredTelemetryPointFilter : ITelemetryPointRegistrationFilter
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<GraphRegisteredTelemetryPointFilter> _logger;

    public GraphRegisteredTelemetryPointFilter(
        IGrainFactory grainFactory,
        ILogger<GraphRegisteredTelemetryPointFilter> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    public async Task<bool> IsRegisteredAsync(TelemetryPointMsg message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.TenantId) || string.IsNullOrWhiteSpace(message.PointId))
        {
            return false;
        }

        try
        {
            var index = _grainFactory.GetGrain<IGraphIndexGrain>(message.TenantId);
            var pointNodeIds = await index.GetByTypeAsync(GraphNodeType.Point);
            if (pointNodeIds.Count == 0)
            {
                return false;
            }

            foreach (var nodeId in pointNodeIds)
            {
                var nodeGrain = _grainFactory.GetGrain<IGraphNodeGrain>(GraphNodeKey.Create(message.TenantId, nodeId));
                var snapshot = await nodeGrain.GetAsync();
                if (snapshot.Node?.Attributes is null)
                {
                    continue;
                }

                if (!snapshot.Node.Attributes.TryGetValue("PointId", out var pointId)
                    || !string.Equals(pointId, message.PointId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (snapshot.Node.Attributes.TryGetValue("DeviceId", out var deviceId)
                    && !string.IsNullOrWhiteSpace(deviceId)
                    && !string.Equals(deviceId, message.DeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to evaluate telemetry registration. TenantId={TenantId}, DeviceId={DeviceId}, PointId={PointId}",
                message.TenantId,
                message.DeviceId,
                message.PointId);
            return false;
        }
    }
}
