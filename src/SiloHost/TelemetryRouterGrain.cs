using System.Collections.Generic;
using Grains.Abstractions;
using Orleans;
using Orleans.Concurrency;

namespace SiloHost;

/// <summary>
/// Stateless worker grain that routes incoming telemetry to the appropriate
/// point grain. Grouping messages per point in a batch reduces the
/// number of RPC calls.
/// </summary>
[StatelessWorker]
public class TelemetryRouterGrain : Grain, ITelemetryRouterGrain
{
    public async Task RouteAsync(TelemetryPointMsg msg)
    {
        var key = PointGrainKey.Create(msg.TenantId, msg.BuildingName, msg.SpaceId, msg.DeviceId, msg.PointId);
        var pointGrain = GrainFactory.GetGrain<IPointGrain>(key);
        await pointGrain.UpsertAsync(msg);
    }

    public async Task RouteBatchAsync(IReadOnlyList<TelemetryPointMsg> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var groups = new Dictionary<(string TenantId, string BuildingName, string SpaceId, string DeviceId, string PointId), List<TelemetryPointMsg>>(batch.Count);
        foreach (var msg in batch)
        {
            var tupleKey = (msg.TenantId, msg.BuildingName, msg.SpaceId, msg.DeviceId, msg.PointId);
            if (!groups.TryGetValue(tupleKey, out var list))
            {
                list = new List<TelemetryPointMsg>();
                groups[tupleKey] = list;
            }

            list.Add(msg);
        }

        foreach (var (tupleKey, messages) in groups)
        {
            var grainKey = PointGrainKey.Create(
                tupleKey.TenantId,
                tupleKey.BuildingName,
                tupleKey.SpaceId,
                tupleKey.DeviceId,
                tupleKey.PointId);
            var grain = GrainFactory.GetGrain<IPointGrain>(grainKey);
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
    Task RouteAsync(TelemetryPointMsg msg);
    Task RouteBatchAsync(IReadOnlyList<TelemetryPointMsg> batch);
}
