using Grains.Abstractions;
using Orleans;

namespace SiloHost;

/// <summary>
/// Stateless worker grain that routes incoming telemetry to the appropriate
/// device grain.  Grouping messages per device in a batch reduces the
/// number of RPC calls.
/// </summary>
[StatelessWorker]
public sealed class TelemetryRouterGrain : Grain, ITelemetryRouterGrain
{
    public async Task RouteAsync(TelemetryMsg msg)
    {
        var key = $"{msg.TenantId}:{msg.DeviceId}";
        var deviceGrain = GrainFactory.GetGrain<IDeviceGrain>(key);
        await deviceGrain.UpsertAsync(msg);
    }

    public async Task RouteBatchAsync(IReadOnlyList<TelemetryMsg> batch)
    {
        foreach (var group in batch.GroupBy(m => $"{m.TenantId}:{m.DeviceId}"))
        {
            var grain = GrainFactory.GetGrain<IDeviceGrain>(group.Key);
            foreach (var msg in group.OrderBy(m => m.Sequence))
            {
                await grain.UpsertAsync(msg);
            }
        }
    }
}

public interface ITelemetryRouterGrain : IGrainWithGuidKey
{
    Task RouteAsync(TelemetryMsg msg);
    Task RouteBatchAsync(IReadOnlyList<TelemetryMsg> batch);
}
