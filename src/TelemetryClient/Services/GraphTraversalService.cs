using System.Text.Json;

namespace TelemetryClient.Services;

/// <summary>
/// Service for graph traversal operations
/// </summary>
public class GraphTraversalService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GraphTraversalService> _logger;

    public GraphTraversalService(HttpClient httpClient, ILogger<GraphTraversalService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<GraphTraversalResult> TraverseAsync(
        string nodeId,
        string tenantId,
        int depth = 1,
        string? predicate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/api/graph/traverse/{Uri.EscapeDataString(nodeId)}?tenantId={tenantId}&depth={depth}";
            if (!string.IsNullOrEmpty(predicate))
            {
                url += $"&predicate={Uri.EscapeDataString(predicate)}";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<GraphTraversalResult>(content, JsonOptions);
            if (result is null)
            {
                return new GraphTraversalResult(null, 0, new List<TraversalNodeSnapshot>());
            }

            var nodes = result.Nodes ?? new List<TraversalNodeSnapshot>();
            return new GraphTraversalResult(result.StartNodeId, result.Depth, nodes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to traverse from node {NodeId}", nodeId);
            return new GraphTraversalResult(null, 0, new List<TraversalNodeSnapshot>());
        }
    }

    public async Task<NodeDetailsDto?> GetNodeAsync(string nodeId, string tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/nodes/{Uri.EscapeDataString(nodeId)}?tenantId={tenantId}", cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Node {NodeId} not found (status: {StatusCode})", nodeId, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<NodeDetailsDto>(content, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch node {NodeId}", nodeId);
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public record GraphTraversalResult(string? StartNodeId, int Depth, List<TraversalNodeSnapshot>? Nodes);

public record TraversalNodeSnapshot(
    TraversalNode Node,
    List<TraversalEdge>? OutgoingEdges,
    List<TraversalEdge>? IncomingEdges);

public record TraversalNode(
    string NodeId,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(GraphNodeTypeStringConverter))] string NodeType,
    string DisplayName,
    Dictionary<string, object>? Attributes);

public record TraversalEdge(
    string TargetNodeId,
    string Predicate);

public record NodeDetailsDto(
    TraversalNode Node,
    List<EdgeDto>? OutgoingEdges,
    List<EdgeDto>? IncomingEdges);

public record EdgeDto(
    string TargetNodeId,
    string Predicate);
