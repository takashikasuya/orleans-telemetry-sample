using System;
using System.Collections.Generic;
using Grains.Abstractions;
using Orleans;

namespace SiloHost;

public sealed class PointControlGrain : Grain, IPointControlGrain
{
    private readonly IPersistentState<PointControlState> _state;

    public PointControlGrain([PersistentState("pointControl", "ControlStore")] IPersistentState<PointControlState> state)
    {
        _state = state;
    }

    public async Task<PointControlSnapshot> SubmitAsync(PointControlRequest request)
    {
        var snapshot = new PointControlSnapshot(
            request.CommandId,
            ControlRequestStatus.Accepted,
            request.DesiredValue,
            request.RequestedAt,
            DateTimeOffset.UtcNow,
            null,
            request.Metadata is not null && request.Metadata.TryGetValue("ConnectorName", out var connector) ? connector : null,
            request.Metadata is not null && request.Metadata.TryGetValue("CorrelationId", out var correlation) ? correlation : null,
            request.Metadata is not null && request.Metadata.TryGetValue("LastError", out var lastError) ? lastError : null);

        _state.State.History[request.CommandId] = snapshot;
        await _state.WriteStateAsync();
        return snapshot;
    }

    public Task<PointControlSnapshot?> GetAsync(string commandId)
    {
        _state.State.History.TryGetValue(commandId, out var snapshot);
        return Task.FromResult(snapshot);
    }

    [GenerateSerializer]
    public sealed class PointControlState
    {
        [Id(0)]
        public Dictionary<string, PointControlSnapshot> History { get; set; } = new();
    }
}
