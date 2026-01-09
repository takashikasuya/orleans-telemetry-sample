using Grains.Abstractions;
using Orleans;
using Orleans.Runtime;

namespace SiloHost;

public sealed class GraphNodeGrain : Grain, IGraphNodeGrain
{
    private readonly IPersistentState<GraphNodeState> _state;

    public GraphNodeGrain([PersistentState("graph-node", "GraphStore")] IPersistentState<GraphNodeState> state)
    {
        _state = state;
    }

    public async Task UpsertAsync(GraphNodeDefinition definition)
    {
        _state.State.Node = definition;
        await _state.WriteStateAsync();
    }

    public async Task AddOutgoingEdgeAsync(GraphEdge edge)
    {
        if (!ContainsEdge(_state.State.OutgoingEdges, edge))
        {
            _state.State.OutgoingEdges.Add(edge);
            await _state.WriteStateAsync();
        }
    }

    public async Task AddIncomingEdgeAsync(GraphEdge edge)
    {
        if (!ContainsEdge(_state.State.IncomingEdges, edge))
        {
            _state.State.IncomingEdges.Add(edge);
            await _state.WriteStateAsync();
        }
    }

    public Task<GraphNodeSnapshot> GetAsync()
    {
        var snapshot = new GraphNodeSnapshot
        {
            Node = _state.State.Node ?? new GraphNodeDefinition { NodeId = this.GetPrimaryKeyString() },
            OutgoingEdges = new List<GraphEdge>(_state.State.OutgoingEdges),
            IncomingEdges = new List<GraphEdge>(_state.State.IncomingEdges)
        };

        return Task.FromResult(snapshot);
    }

    private static bool ContainsEdge(List<GraphEdge> edges, GraphEdge edge)
    {
        return edges.Any(e => e.Predicate == edge.Predicate && e.TargetNodeId == edge.TargetNodeId);
    }

    [GenerateSerializer]
    public sealed class GraphNodeState
    {
        [Id(0)] public GraphNodeDefinition? Node { get; set; }
        [Id(1)] public List<GraphEdge> OutgoingEdges { get; set; } = new();
        [Id(2)] public List<GraphEdge> IncomingEdges { get; set; } = new();
    }
}
