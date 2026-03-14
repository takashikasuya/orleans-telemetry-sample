using TelemetryClient.Models;
using TelemetryClient.Services;

namespace TelemetryClient.Services;

/// <summary>
/// Service for building and managing the hierarchy tree
/// </summary>
public class HierarchyTreeService
{
    private readonly RegistryService _registryService;
    private readonly GraphTraversalService _graphTraversalService;
    private readonly ILogger<HierarchyTreeService> _logger;

    public HierarchyTreeService(
        RegistryService registryService,
        GraphTraversalService graphTraversalService,
        ILogger<HierarchyTreeService> logger)
    {
        _registryService = registryService;
        _graphTraversalService = graphTraversalService;
        _logger = logger;
    }

    /// <summary>
    /// Load root nodes (Sites) for the tree
    /// </summary>
    public async Task<List<HierarchyTreeNode>> LoadRootNodesAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var sites = await _registryService.GetSitesAsync(tenantId, cancellationToken);
            
            return sites.Select(site => new HierarchyTreeNode
            {
                NodeId = site.NodeId,
                NodeType = site.NodeType,
                DisplayName = site.DisplayName ?? site.Name ?? site.NodeId,
                Attributes = site.Attributes,
                ChildrenLoaded = false
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load root nodes for tenant {TenantId}", tenantId);
            return new List<HierarchyTreeNode>();
        }
    }

    /// <summary>
    /// Load children for a given node using graph traversal
    /// </summary>
    public async Task<List<HierarchyTreeNode>> LoadChildrenAsync(
        string nodeId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use depth=1 to get immediate children
            var result = await _graphTraversalService.TraverseAsync(nodeId, tenantId, depth: 1, cancellationToken: cancellationToken);
            
            // Find edges originating from this node
            var startNode = result.Nodes?.FirstOrDefault(n => n.Node.NodeId == nodeId);
            var childNodeIds = (startNode?.OutgoingEdges ?? Enumerable.Empty<TraversalEdge>())
                .Select(e => e.TargetNodeId)
                .Distinct()
                .ToList();

            // Map nodes to tree nodes
            var children = (result.Nodes ?? Enumerable.Empty<TraversalNodeSnapshot>())
                .Select(n => n.Node)
                .Where(n => childNodeIds.Contains(n.NodeId))
                .Select(n => new HierarchyTreeNode
                {
                    NodeId = n.NodeId,
                    NodeType = n.NodeType,
                    DisplayName = string.IsNullOrWhiteSpace(n.DisplayName) ? n.NodeId : n.DisplayName,
                    Attributes = n.Attributes,
                    ChildrenLoaded = false
                })
                .OrderBy(n => n.DisplayName)
                .ToList();

            return children;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load children for node {NodeId}", nodeId);
            return new List<HierarchyTreeNode>();
        }
    }

    /// <summary>
    /// Search nodes by name or ID across the hierarchy
    /// </summary>
    public List<HierarchyTreeNode> SearchNodes(List<HierarchyTreeNode> nodes, string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return nodes;

        var results = new List<HierarchyTreeNode>();
        var lowerSearchTerm = searchTerm.ToLowerInvariant();

        foreach (var node in nodes)
        {
            if (node.DisplayName.ToLowerInvariant().Contains(lowerSearchTerm) ||
                node.NodeId.ToLowerInvariant().Contains(lowerSearchTerm))
            {
                results.Add(node);
            }

            if (node.Children.Any())
            {
                results.AddRange(SearchNodes(node.Children, searchTerm));
            }
        }

        return results;
    }
}
