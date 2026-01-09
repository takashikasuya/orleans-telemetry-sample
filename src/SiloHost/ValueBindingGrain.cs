using Grains.Abstractions;
using Orleans;
using Orleans.Runtime;

namespace SiloHost;

public sealed class ValueBindingGrain : Grain, IValueBindingGrain
{
    private readonly IPersistentState<NodeValueState> _state;

    public ValueBindingGrain([PersistentState("node-values", "ValueStore")] IPersistentState<NodeValueState> state)
    {
        _state = state;
    }

    public async Task UpsertAsync(NodeValueUpdate update)
    {
        if (update.Sequence <= _state.State.LastSequence)
        {
            return;
        }

        foreach (var kv in update.Values)
        {
            _state.State.Values[kv.Key] = kv.Value;
        }

        _state.State.LastSequence = update.Sequence;
        _state.State.UpdatedAt = update.Timestamp.ToUniversalTime();
        await _state.WriteStateAsync();
    }

    public Task<NodeValueSnapshot> GetAsync()
    {
        return Task.FromResult(new NodeValueSnapshot
        {
            LastSequence = _state.State.LastSequence,
            Values = new Dictionary<string, object>(_state.State.Values),
            UpdatedAt = _state.State.UpdatedAt
        });
    }

    [GenerateSerializer]
    public sealed class NodeValueState
    {
        [Id(0)] public long LastSequence { get; set; }
        [Id(1)] public Dictionary<string, object> Values { get; set; } = new();
        [Id(2)] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.MinValue;
    }
}
