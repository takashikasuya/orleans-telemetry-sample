using System.Collections.Generic;
using Grains.Abstractions;
using Orleans;
using Orleans.Concurrency;

namespace SiloHost;

/// <summary>
/// Stateless worker grain that routes incoming telemetry to the appropriate
/// device grain.  Grouping messages per device in a batch reduces the
/// number of RPC calls.
/// </summary>
[StatelessWorker]
public class TelemetryRouterGrain : Grain, ITelemetryRouterGrain
{
    public async Task RouteAsync(TelemetryMsg msg)
    {
        var key = DeviceGrainKey.Create(msg.TenantId, msg.DeviceId);
        var deviceGrain = GrainFactory.GetGrain<IDeviceGrain>(key);
        await deviceGrain.UpsertAsync(msg);
    }

    public async Task RouteBatchAsync(IReadOnlyList<TelemetryMsg> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var groups = new Dictionary<(string TenantId, string DeviceId), List<TelemetryMsg>>(batch.Count);
        foreach (var msg in batch)
        {
            var tupleKey = (msg.TenantId, msg.DeviceId);
            if (!groups.TryGetValue(tupleKey, out var list))
            {
                list = new List<TelemetryMsg>();
                groups[tupleKey] = list;
            }

            list.Add(msg);
        }

        foreach (var (tupleKey, messages) in groups)
        {
            var grainKey = DeviceGrainKey.Create(tupleKey.TenantId, tupleKey.DeviceId);
            var grain = GrainFactory.GetGrain<IDeviceGrain>(grainKey);
            messages.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
            foreach (var msg in messages)
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
