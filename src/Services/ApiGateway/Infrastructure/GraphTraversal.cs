using Grains.Abstractions;
using Orleans;

namespace ApiGateway.Infrastructure;

internal sealed class GraphTraversal
{
    public async Task<GraphTraversalResult> TraverseAsync(
        IClusterClient client,
        string tenant,
        string startNodeId,
        int depth,
        string? predicate)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string NodeId, int Depth)>();
        var nodes = new List<GraphNodeSnapshot>();

        visited.Add(startNodeId);
        queue.Enqueue((startNodeId, 0));

        while (queue.Count > 0)
        {
            var (nodeId, level) = queue.Dequeue();
            var grainKey = GraphNodeKey.Create(tenant, nodeId);
            var grain = client.GetGrain<IGraphNodeGrain>(grainKey);
            var snapshot = await grain.GetAsync();
            nodes.Add(snapshot);

            if (level >= depth)
            {
                continue;
            }

            foreach (var edge in snapshot.OutgoingEdges)
            {
                if (!string.IsNullOrWhiteSpace(predicate) && edge.Predicate != predicate)
                {
                    continue;
                }

                if (visited.Add(edge.TargetNodeId))
                {
                    queue.Enqueue((edge.TargetNodeId, level + 1));
                }
            }
        }

        return new GraphTraversalResult
        {
            StartNodeId = startNodeId,
            Depth = depth,
            Nodes = nodes
        };
    }
}

internal sealed class GraphTraversalResult
{
    public string StartNodeId { get; set; } = string.Empty;
    public int Depth { get; set; }
    public IReadOnlyList<GraphNodeSnapshot> Nodes { get; set; } = Array.Empty<GraphNodeSnapshot>();
}
