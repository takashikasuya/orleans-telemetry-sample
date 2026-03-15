using System;
using System.Collections.Generic;
using Grains.Abstractions;
using Orleans;

namespace SiloHost;

public sealed class DeviceControlIndexGrain : Grain, IDeviceControlIndexGrain
{
    private readonly IPersistentState<DeviceControlIndexState> _state;

    public DeviceControlIndexGrain(
        [PersistentState("deviceControlIndex", "ControlStore")] IPersistentState<DeviceControlIndexState> state)
    {
        _state = state;
    }

    public async Task RecordAsync(string commandId, PointControlSnapshot snapshot)
    {
        _state.State.Commands[commandId] = snapshot;
        await _state.WriteStateAsync();
    }

    public Task<PointControlSnapshot?> GetAsync(string commandId)
    {
        _state.State.Commands.TryGetValue(commandId, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public async Task UpdateAsync(string commandId, ControlRequestStatus status, string? correlationId, string? lastError)
    {
        if (!_state.State.Commands.TryGetValue(commandId, out var existing))
        {
            return;
        }

        var updated = existing with
        {
            Status = status,
            CorrelationId = correlationId ?? existing.CorrelationId,
            LastError = lastError ?? existing.LastError
        };

        _state.State.Commands[commandId] = updated;
        await _state.WriteStateAsync();
    }

    [GenerateSerializer]
    public sealed class DeviceControlIndexState
    {
        [Id(0)]
        public Dictionary<string, PointControlSnapshot> Commands { get; set; } = new();
    }
}
