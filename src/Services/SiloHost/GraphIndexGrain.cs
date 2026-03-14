using Grains.Abstractions;
using Orleans;
using Orleans.Runtime;

namespace SiloHost;

public sealed class GraphIndexGrain : Grain, IGraphIndexGrain
{
    private readonly IPersistentState<GraphIndexState> _state;

    public GraphIndexGrain([PersistentState("graph-index", "GraphIndexStore")] IPersistentState<GraphIndexState> state)
    {
        _state = state;
    }

    public async Task AddNodeAsync(GraphNodeDefinition definition)
    {
        var list = GetOrCreateList(definition.NodeType);
        if (!list.Contains(definition.NodeId))
        {
            list.Add(definition.NodeId);
            await _state.WriteStateAsync();
        }
    }

    public async Task RemoveNodeAsync(string nodeId, GraphNodeType nodeType)
    {
        if (_state.State.ByType.TryGetValue(nodeType, out var list) && list.Remove(nodeId))
        {
            await _state.WriteStateAsync();
        }
    }

    public Task<IReadOnlyList<string>> GetByTypeAsync(GraphNodeType nodeType)
    {
        if (_state.State.ByType.TryGetValue(nodeType, out var list))
        {
            return Task.FromResult<IReadOnlyList<string>>(list.ToArray());
        }

        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private List<string> GetOrCreateList(GraphNodeType nodeType)
    {
        if (!_state.State.ByType.TryGetValue(nodeType, out var list))
        {
            list = new List<string>();
            _state.State.ByType[nodeType] = list;
        }

        return list;
    }

    [GenerateSerializer]
    public sealed class GraphIndexState
    {
        [Id(0)] public Dictionary<GraphNodeType, List<string>> ByType { get; set; } = new();
    }
}
