using Orleans;

namespace Grains.Abstractions;

public enum GraphNodeType
{
    Unknown = 0,
    Site = 1,
    Building = 2,
    Level = 3,
    Area = 4,
    Equipment = 5,
    Device = 6,
    Point = 7
}

[GenerateSerializer]
public sealed class GraphNodeDefinition
{
    [Id(0)] public string NodeId { get; set; } = string.Empty;
    [Id(1)] public GraphNodeType NodeType { get; set; } = GraphNodeType.Unknown;
    [Id(2)] public string DisplayName { get; set; } = string.Empty;
    [Id(3)] public Dictionary<string, string> Attributes { get; set; } = new();
}

[GenerateSerializer]
public sealed class GraphEdge
{
    [Id(0)] public string Predicate { get; set; } = string.Empty;
    [Id(1)] public string TargetNodeId { get; set; } = string.Empty;
}

[GenerateSerializer]
public sealed class GraphNodeSnapshot
{
    [Id(0)] public GraphNodeDefinition Node { get; set; } = new();
    [Id(1)] public List<GraphEdge> OutgoingEdges { get; set; } = new();
    [Id(2)] public List<GraphEdge> IncomingEdges { get; set; } = new();
}

[GenerateSerializer]
public sealed class NodeValueUpdate
{
    [Id(0)] public long Sequence { get; set; }
    [Id(1)] public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    [Id(2)] public Dictionary<string, object> Values { get; set; } = new();
}

[GenerateSerializer]
public sealed class NodeValueSnapshot
{
    [Id(0)] public long LastSequence { get; set; }
    [Id(1)] public IReadOnlyDictionary<string, object> Values { get; set; } = new Dictionary<string, object>();
    [Id(2)] public DateTimeOffset UpdatedAt { get; set; }
}

[GenerateSerializer]
public sealed class GraphSeedData
{
    [Id(0)] public List<GraphNodeDefinition> Nodes { get; set; } = new();
    [Id(1)] public List<GraphSeedEdge> Edges { get; set; } = new();
}

[GenerateSerializer]
public sealed class GraphSeedEdge
{
    [Id(0)] public string SourceNodeId { get; set; } = string.Empty;
    [Id(1)] public string Predicate { get; set; } = string.Empty;
    [Id(2)] public string TargetNodeId { get; set; } = string.Empty;
}

public static class GraphNodeKey
{
    public static string Create(string tenantId, string nodeId) => $"{tenantId}:{nodeId}";
}

public interface IGraphNodeGrain : IGrainWithStringKey
{
    Task UpsertAsync(GraphNodeDefinition definition);
    Task AddOutgoingEdgeAsync(GraphEdge edge);
    Task AddIncomingEdgeAsync(GraphEdge edge);
    Task<GraphNodeSnapshot> GetAsync();
}

public interface IValueBindingGrain : IGrainWithStringKey
{
    Task UpsertAsync(NodeValueUpdate update);
    Task<NodeValueSnapshot> GetAsync();
}

public interface IGraphIndexGrain : IGrainWithStringKey
{
    Task AddNodeAsync(GraphNodeDefinition definition);
    Task RemoveNodeAsync(string nodeId, GraphNodeType nodeType);
    Task<IReadOnlyList<string>> GetByTypeAsync(GraphNodeType nodeType);
}
