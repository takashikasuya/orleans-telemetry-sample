using Grains.Abstractions;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace SiloHost;

/// <summary>
/// Grain storing the latest telemetry for a device.  This grain holds the
/// most recently observed property values and sequence number.  When
/// updated it emits a snapshot to an Orleans stream so that the API can
/// push changes to connected clients.
/// </summary>
public sealed class DeviceGrain : Grain, IDeviceGrain
{
    private readonly IPersistentState<DeviceState> _state;
    private IAsyncStream<DeviceSnapshot>? _stream;

    public DeviceGrain([PersistentState("device", "DeviceStore")] IPersistentState<DeviceState> state)
    {
        _state = state;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        TryInitStream();
        return Task.CompletedTask;
    }

    private void TryInitStream()
    {
        if (_stream is not null)
        {
            return;
        }

        try
        {
            var provider = this.GetStreamProvider("DeviceUpdates");
            var streamId = StreamId.Create("DeviceUpdatesNs", this.GetPrimaryKeyString());
            _stream = provider.GetStream<DeviceSnapshot>(streamId);
        }
        catch
        {
            _stream = null;
        }
    }

    public async Task UpsertAsync(TelemetryMsg msg)
    {
        // drop stale or duplicate messages
        if (msg.Sequence <= _state.State.LastSequence)
        {
            return;
        }
        // update state
        foreach (var kv in msg.Properties)
        {
            _state.State.LatestProps[kv.Key] = kv.Value;
        }
        _state.State.LastSequence = msg.Sequence;
        _state.State.UpdatedAt = msg.Timestamp.ToUniversalTime();
        await _state.WriteStateAsync();
        // emit snapshot
        TryInitStream();
        if (_stream is not null)
        {
            var snap = new DeviceSnapshot(
                _state.State.LastSequence,
                _state.State.LatestProps,
                _state.State.UpdatedAt);
            await _stream.OnNextAsync(snap);
        }
    }

    public Task<DeviceSnapshot> GetAsync()
    {
        return Task.FromResult(new DeviceSnapshot(
            _state.State.LastSequence,
            _state.State.LatestProps,
            _state.State.UpdatedAt));
    }

    [GenerateSerializer]
    public class DeviceState
    {
        [Id(0)] public long LastSequence { get; set; }
        [Id(1)] public Dictionary<string, object> LatestProps { get; set; } = new();
        [Id(2)] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.MinValue;
    }
}
