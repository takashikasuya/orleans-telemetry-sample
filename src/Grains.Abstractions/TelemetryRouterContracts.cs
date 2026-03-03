using Orleans;

namespace Grains.Abstractions;

/// <summary>
/// Grain contract for routing telemetry point messages to destination grains.
/// </summary>
public interface ITelemetryRouterGrain : IGrainWithGuidKey
{
    /// <summary>
    /// Routes a single telemetry point message.
    /// </summary>
    /// <param name="msg">Telemetry point message.</param>
    Task RouteAsync(TelemetryPointMsg msg);

    /// <summary>
    /// Routes a batch of telemetry point messages.
    /// </summary>
    /// <param name="batch">Telemetry point message batch.</param>
    Task RouteBatchAsync(IReadOnlyList<TelemetryPointMsg> batch);
}
