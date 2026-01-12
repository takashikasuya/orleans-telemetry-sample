using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text.Json.Serialization;
using Grains.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace ApiGateway.Services;

internal sealed class GraphRegistryService
{
    private readonly IClusterClient _client;
    private readonly RegistryExportService _exports;
    private readonly RegistryExportOptions _options;
    private readonly ILogger<GraphRegistryService> _logger;

    public GraphRegistryService(
        IClusterClient client,
        RegistryExportService exports,
        IOptions<RegistryExportOptions> options,
        ILogger<GraphRegistryService> logger)
    {
        _client = client;
        _exports = exports;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RegistryQueryResponse> GetNodesAsync(
        string tenant,
        GraphNodeType nodeType,
        int? limit,
        CancellationToken ct)
    {
        var index = _client.GetGrain<IGraphIndexGrain>(tenant);
        var nodeIds = await index.GetByTypeAsync(nodeType);
        var totalCount = nodeIds.Count;

        if (totalCount == 0 || (limit.HasValue && limit.Value <= 0))
        {
            return RegistryQueryResponse.Empty(totalCount);
        }

        if (!limit.HasValue && totalCount > _options.MaxInlineRecords)
        {
            _logger.LogInformation("Exporting {NodeType} registry for tenant {Tenant} with {Count} entries.", nodeType, tenant, totalCount);
            var exportedNodes = await ResolveNodesAsync(tenant, nodeIds, ct);
            var reference = await _exports.CreateExportAsync(
                new RegistryExportRequest(tenant, nodeType, totalCount),
                exportedNodes,
                ct);
            return RegistryQueryResponse.UrlResult(reference.Url, reference.ExpiresAt, totalCount, reference.Count);
        }

        var requestedLimit = limit.HasValue ? Math.Min(limit.Value, totalCount) : totalCount;
        var selectedIds = nodeIds.Take(requestedLimit).ToList();
        var inlineNodes = await ResolveNodesAsync(tenant, selectedIds, ct);
        return RegistryQueryResponse.Inline(inlineNodes, totalCount);
    }

    private async Task<List<RegistryNodeSummary>> ResolveNodesAsync(string tenant, IReadOnlyList<string> nodeIds, CancellationToken ct)
    {
        var nodes = new List<RegistryNodeSummary>(nodeIds.Count);
        foreach (var nodeId in nodeIds)
        {
            ct.ThrowIfCancellationRequested();
            var grainKey = GraphNodeKey.Create(tenant, nodeId);
            var grain = _client.GetGrain<IGraphNodeGrain>(grainKey);
            var snapshot = await grain.GetAsync();
            var definition = snapshot.Node ?? new GraphNodeDefinition
            {
                NodeId = nodeId,
                NodeType = GraphNodeType.Unknown
            };

            var attributes = definition.Attributes is not null
                ? new Dictionary<string, string>(definition.Attributes)
                : new Dictionary<string, string>();

            nodes.Add(new RegistryNodeSummary(
                definition.NodeId,
                definition.NodeType,
                definition.DisplayName,
                attributes));
        }

        return nodes;
    }
}

internal sealed record RegistryNodeSummary(
    string NodeId,
    GraphNodeType NodeType,
    string DisplayName,
    IReadOnlyDictionary<string, string> Attributes);

internal sealed record RegistryQueryResponse
{
    public string Mode { get; init; } = "inline";
    public int Count { get; init; }
    public int TotalCount { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RegistryNodeSummary>? Items { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; init; }

    public static RegistryQueryResponse Empty(int totalCount = 0)
        => new()
        {
            Items = Array.Empty<RegistryNodeSummary>(),
            Count = 0,
            TotalCount = totalCount
        };

    public static RegistryQueryResponse Inline(IReadOnlyList<RegistryNodeSummary> items, int totalCount)
        => new()
        {
            Items = items,
            Count = items.Count,
            TotalCount = totalCount
        };

    public static RegistryQueryResponse UrlResult(string url, DateTimeOffset expiresAt, int totalCount, int count)
        => new()
        {
            Mode = "url",
            Url = url,
            ExpiresAt = expiresAt,
            Count = count,
            TotalCount = totalCount
        };
}
