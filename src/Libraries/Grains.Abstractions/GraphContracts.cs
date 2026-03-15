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

/// <summary>
/// Defines graph node metadata used by graph grains.
/// </summary>
[GenerateSerializer]
public sealed class GraphNodeDefinition
{
    /// <summary>
    /// Gets or sets the unique node identifier.
    /// </summary>
    [Id(0)] public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the node type.
    /// </summary>
    [Id(1)] public GraphNodeType NodeType { get; set; } = GraphNodeType.Unknown;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    [Id(2)] public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets additional node attributes.
    /// </summary>
    [Id(3)] public Dictionary<string, string> Attributes { get; set; } = new();
}

/// <summary>
/// Represents an edge to another graph node.
/// </summary>
[GenerateSerializer]
public sealed class GraphEdge
{
    /// <summary>
    /// Gets or sets the edge predicate.
    /// </summary>
    [Id(0)] public string Predicate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target node identifier.
    /// </summary>
    [Id(1)] public string TargetNodeId { get; set; } = string.Empty;
}

/// <summary>
/// Represents a graph node and its connected edges.
/// </summary>
[GenerateSerializer]
public sealed class GraphNodeSnapshot
{
    /// <summary>
    /// Gets or sets the node definition.
    /// </summary>
    [Id(0)] public GraphNodeDefinition Node { get; set; } = new();

    /// <summary>
    /// Gets or sets outgoing edges.
    /// </summary>
    [Id(1)] public List<GraphEdge> OutgoingEdges { get; set; } = new();

    /// <summary>
    /// Gets or sets incoming edges.
    /// </summary>
    [Id(2)] public List<GraphEdge> IncomingEdges { get; set; } = new();
}

/// <summary>
/// Contains graph seed data for batch initialization.
/// </summary>
[GenerateSerializer]
public sealed class GraphSeedData
{
    /// <summary>
    /// Gets or sets nodes to initialize.
    /// </summary>
    [Id(0)] public List<GraphNodeDefinition> Nodes { get; set; } = new();

    /// <summary>
    /// Gets or sets edges to initialize.
    /// </summary>
    [Id(1)] public List<GraphSeedEdge> Edges { get; set; } = new();
}

/// <summary>
/// Represents an edge definition in seed data.
/// </summary>
[GenerateSerializer]
public sealed class GraphSeedEdge
{
    /// <summary>
    /// Gets or sets the source node identifier.
    /// </summary>
    [Id(0)] public string SourceNodeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the edge predicate.
    /// </summary>
    [Id(1)] public string Predicate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target node identifier.
    /// </summary>
    [Id(2)] public string TargetNodeId { get; set; } = string.Empty;
}

/// <summary>
/// Builds grain keys for graph node grains.
/// </summary>
public static class GraphNodeKey
{
    /// <summary>
    /// Creates a graph node grain key.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="nodeId">Node identifier.</param>
    /// <returns>Composite graph node grain key.</returns>
    public static string Create(string tenantId, string nodeId) => $"{tenantId}:{nodeId}";
}

/// <summary>
/// Grain contract for a single graph node.
/// </summary>
public interface IGraphNodeGrain : IGrainWithStringKey
{
    /// <summary>
    /// Upserts a node definition.
    /// </summary>
    /// <param name="definition">Node definition to apply.</param>
    Task UpsertAsync(GraphNodeDefinition definition);

    /// <summary>
    /// Adds an outgoing edge.
    /// </summary>
    /// <param name="edge">Edge to add.</param>
    Task AddOutgoingEdgeAsync(GraphEdge edge);

    /// <summary>
    /// Adds an incoming edge.
    /// </summary>
    /// <param name="edge">Edge to add.</param>
    Task AddIncomingEdgeAsync(GraphEdge edge);

    /// <summary>
    /// Gets the current node snapshot.
    /// </summary>
    /// <returns>Graph node snapshot.</returns>
    Task<GraphNodeSnapshot> GetAsync();
}

/// <summary>
/// Grain contract for graph node indexes by type.
/// </summary>
public interface IGraphIndexGrain : IGrainWithStringKey
{
    /// <summary>
    /// Adds a node to type index.
    /// </summary>
    /// <param name="definition">Node definition to index.</param>
    Task AddNodeAsync(GraphNodeDefinition definition);

    /// <summary>
    /// Removes a node from type index.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <param name="nodeType">Node type.</param>
    Task RemoveNodeAsync(string nodeId, GraphNodeType nodeType);

    /// <summary>
    /// Gets node identifiers for the specified type.
    /// </summary>
    /// <param name="nodeType">Node type.</param>
    /// <returns>Node identifier list.</returns>
    Task<IReadOnlyList<string>> GetByTypeAsync(GraphNodeType nodeType);
}


/// <summary>
/// Represents tenant metadata stored in graph registry.
/// </summary>
[GenerateSerializer]
public sealed class GraphTenantInfo
{
    /// <summary>
    /// Gets or sets tenant identifier.
    /// </summary>
    [Id(0)] public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets tenant display name.
    /// </summary>
    [Id(1)] public string TenantName { get; set; } = string.Empty;
}

/// <summary>
/// Grain contract for tenant registration in graph subsystem.
/// </summary>
public interface IGraphTenantRegistryGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// Registers a tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="tenantName">Optional tenant display name.</param>
    Task RegisterTenantAsync(string tenantId, string? tenantName = null);

    /// <summary>
    /// Gets all registered tenant identifiers.
    /// </summary>
    /// <returns>Tenant identifier list.</returns>
    Task<IReadOnlyList<string>> GetTenantIdsAsync();

    /// <summary>
    /// Gets all registered tenant details.
    /// </summary>
    /// <returns>Tenant information list.</returns>
    Task<IReadOnlyList<GraphTenantInfo>> GetTenantsAsync();
}


/// <summary>
/// Grain contract for graph tag indexing.
/// </summary>
public interface IGraphTagIndexGrain : IGrainWithStringKey
{
    /// <summary>
    /// Indexes a node by tags.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <param name="attributes">Node attributes used as tags.</param>
    Task IndexNodeAsync(string nodeId, Dictionary<string, string> attributes);

    /// <summary>
    /// Removes a node from tag index.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    Task RemoveNodeAsync(string nodeId);

    /// <summary>
    /// Gets node identifiers matching all specified tags.
    /// </summary>
    /// <param name="tags">Tags to match.</param>
    /// <returns>Matching node identifier list.</returns>
    Task<IReadOnlyList<string>> GetNodeIdsByTagsAsync(IReadOnlyList<string> tags);
}

