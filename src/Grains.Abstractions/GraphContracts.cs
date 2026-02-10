using System.Collections.Generic;
using Orleans;

namespace Grains.Abstractions;

/// <summary>
/// Represents the type of a node in the graph.
/// </summary>
public enum GraphNodeType
{
    /// <summary>
    /// Unknown node type.
    /// </summary>
    Unknown = 0,
    /// <summary>
    /// Site node type.
    /// </summary>
    Site = 1,
    /// <summary>
    /// Building node type.
    /// </summary>
    Building = 2,
    /// <summary>
    /// Level node type.
    /// </summary>
    Level = 3,
    /// <summary>
    /// Area node type.
    /// </summary>
    Area = 4,
    /// <summary>
    /// Equipment node type.
    /// </summary>
    Equipment = 5,
    /// <summary>
    /// Device node type.
    /// </summary>
    Device = 6,
    /// <summary>
    /// Point node type.
    /// </summary>
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

public interface IGraphIndexGrain : IGrainWithStringKey
{
    Task AddNodeAsync(GraphNodeDefinition definition);
    Task RemoveNodeAsync(string nodeId, GraphNodeType nodeType);
    Task<IReadOnlyList<string>> GetByTypeAsync(GraphNodeType nodeType);
}

public interface IGraphTenantRegistryGrain : IGrainWithIntegerKey
{
    Task RegisterTenantAsync(string tenantId);
    Task<IReadOnlyList<string>> GetTenantIdsAsync();
}


public interface IGraphTagIndexGrain : IGrainWithStringKey
{
    Task IndexNodeAsync(string nodeId, Dictionary<string, string> attributes);
    Task RemoveNodeAsync(string nodeId);
    Task<IReadOnlyList<string>> GetNodeIdsByTagsAsync(IReadOnlyList<string> tags);
}

