using Grains.Abstractions;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace SiloHost;

/// <summary>
/// Grain storing the latest telemetry for a single point. This grain holds
/// the most recently observed value and sequence number. When updated it
/// emits a snapshot to an Orleans stream so that the API can push changes.
/// </summary>
public sealed class PointGrain : Grain, IPointGrain
{
    private readonly IPersistentState<PointState> _state;
    private IAsyncStream<PointSnapshot>? _stream;

    public PointGrain([PersistentState("point", "PointStore")] IPersistentState<PointState> state)
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
            var provider = this.GetStreamProvider("PointUpdates");
            var streamId = StreamId.Create("PointUpdatesNs", this.GetPrimaryKeyString());
            _stream = provider.GetStream<PointSnapshot>(streamId);
        }
        catch
        {
            _stream = null;
        }
    }

    public async Task UpsertAsync(TelemetryPointMsg msg)
    {
        if (msg.Sequence <= _state.State.LastSequence)
        {
            return;
        }

        _state.State.LastSequence = msg.Sequence;
        _state.State.LatestValue = msg.Value;
        _state.State.UpdatedAt = msg.Timestamp.ToUniversalTime();
        await _state.WriteStateAsync();

        TryInitStream();
        if (_stream is not null)
        {
            var snap = new PointSnapshot(
                _state.State.LastSequence,
                _state.State.LatestValue,
                _state.State.UpdatedAt);
            await _stream.OnNextAsync(snap);
        }
    }

    public Task<PointSnapshot> GetAsync()
    {
        return Task.FromResult(new PointSnapshot(
            _state.State.LastSequence,
            _state.State.LatestValue,
            _state.State.UpdatedAt));
    }

    public Task<IReadOnlyList<PointSample>> GetRecentSamplesAsync(int maxSamples = 100)
    {
        // Ring buffer removed for memory efficiency.
        // Hot data should be cached browser-side via SignalR streaming.
        return Task.FromResult<IReadOnlyList<PointSample>>(Array.Empty<PointSample>());
    }

    [GenerateSerializer]
    public sealed class PointState
    {
        [Id(0)] public long LastSequence { get; set; }
        [Id(1)] public object? LatestValue { get; set; }
        [Id(2)] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.MinValue;
    }
}
