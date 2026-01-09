using Orleans;

namespace Grains.Abstractions;

public interface ITelemetryRouterGrain : IGrainWithGuidKey
{
    Task RouteAsync(TelemetryPointMsg msg);
    Task RouteBatchAsync(IReadOnlyList<TelemetryPointMsg> batch);
}
