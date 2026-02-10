using System;
using System.Threading;
using System.Threading.Tasks;
using DataModel.Analyzer.Integration;
using Grains.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;

namespace SiloHost;

internal sealed class GraphSeeder
{
    private readonly OrleansIntegrationService _integration;
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<GraphSeeder> _logger;

    public GraphSeeder(
        OrleansIntegrationService integration,
        IGrainFactory grainFactory,
        ILogger<GraphSeeder> logger)
    {
        _integration = integration;
        _grainFactory = grainFactory;
        _logger = logger;
    }

    public async Task<GraphSeedStatus> SeedAsync(string path, string tenantId, CancellationToken cancellationToken)
    {
        tenantId = string.IsNullOrWhiteSpace(tenantId) ? "default" : tenantId;
        var start = DateTimeOffset.UtcNow;
        try
        {
            var seed = await _integration.ExtractGraphSeedDataAsync(path);
            return await ApplySeedAsync(seed, path, tenantId, start, cancellationToken);
        }
        catch (Exception ex)
        {
            var failedTime = DateTimeOffset.UtcNow;
            _logger.LogError(ex, "Graph seed failed for {Path} (tenant: {Tenant})", path, tenantId);
            return new GraphSeedStatus
            {
                Success = false,
                TenantId = tenantId,
                Path = path,
                StartedAt = start,
                CompletedAt = failedTime,
                NodeCount = 0,
                EdgeCount = 0,
                Message = ex.Message
            };
        }
    }

    public async Task<GraphSeedStatus> SeedFromContentAsync(string content, string sourceName, string tenantId, CancellationToken cancellationToken)
    {
        tenantId = string.IsNullOrWhiteSpace(tenantId) ? "default" : tenantId;
        var start = DateTimeOffset.UtcNow;
        try
        {
            var seed = await _integration.ExtractGraphSeedDataFromContentAsync(content, sourceName);
            return await ApplySeedAsync(seed, sourceName, tenantId, start, cancellationToken);
        }
        catch (Exception ex)
        {
            var failedTime = DateTimeOffset.UtcNow;
            _logger.LogError(ex, "Graph seed failed for {Path} (tenant: {Tenant})", sourceName, tenantId);
            return new GraphSeedStatus
            {
                Success = false,
                TenantId = tenantId,
                Path = sourceName,
                StartedAt = start,
                CompletedAt = failedTime,
                NodeCount = 0,
                EdgeCount = 0,
                Message = ex.Message
            };
        }
    }

    private async Task<GraphSeedStatus> ApplySeedAsync(
        GraphSeedData seed,
        string sourceName,
        string tenantId,
        DateTimeOffset start,
        CancellationToken cancellationToken)
    {
        try
        {
            var nodeCounts = seed.Nodes.GroupBy(n => n.NodeType)
                .ToDictionary(g => g.Key, g => g.Count());
            _logger.LogInformation("Graph seed nodes by type: {NodeCounts}", nodeCounts);
            if (!nodeCounts.TryGetValue(GraphNodeType.Area, out var areaCount))
            {
                areaCount = 0;
            }
            _logger.LogInformation("Graph seed Area count: {AreaCount}", areaCount);
            var index = _grainFactory.GetGrain<IGraphIndexGrain>(tenantId);
            var tagIndex = _grainFactory.GetGrain<IGraphTagIndexGrain>(tenantId);
            var registry = _grainFactory.GetGrain<IGraphTenantRegistryGrain>(0);

            foreach (var node in seed.Nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = GraphNodeKey.Create(tenantId, node.NodeId);
                var grain = _grainFactory.GetGrain<IGraphNodeGrain>(key);
                await grain.UpsertAsync(node);
                await index.AddNodeAsync(node);
                await tagIndex.IndexNodeAsync(node.NodeId, node.Attributes);
            }

            foreach (var edge in seed.Edges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceKey = GraphNodeKey.Create(tenantId, edge.SourceNodeId);
                var targetKey = GraphNodeKey.Create(tenantId, edge.TargetNodeId);
                var source = _grainFactory.GetGrain<IGraphNodeGrain>(sourceKey);
                var target = _grainFactory.GetGrain<IGraphNodeGrain>(targetKey);
                var outgoing = new GraphEdge { Predicate = edge.Predicate, TargetNodeId = edge.TargetNodeId };
                var incoming = new GraphEdge { Predicate = edge.Predicate, TargetNodeId = edge.SourceNodeId };
                await source.AddOutgoingEdgeAsync(outgoing);
                await target.AddIncomingEdgeAsync(incoming);
            }

            var completed = DateTimeOffset.UtcNow;
            await registry.RegisterTenantAsync(tenantId);
            _logger.LogInformation("Graph seed completed. Path={Path} Tenant={Tenant} Nodes={NodeCount} Edges={EdgeCount}",
                sourceName,
                tenantId,
                seed.Nodes.Count,
                seed.Edges.Count);

            return new GraphSeedStatus
            {
                Success = true,
                TenantId = tenantId,
                Path = sourceName,
                StartedAt = start,
                CompletedAt = completed,
                NodeCount = seed.Nodes.Count,
                EdgeCount = seed.Edges.Count
            };
        }
        catch (Exception ex)
        {
            var failedTime = DateTimeOffset.UtcNow;
            _logger.LogError(ex, "Graph seed failed for {Path} (tenant: {Tenant})", sourceName, tenantId);
            return new GraphSeedStatus
            {
                Success = false,
                TenantId = tenantId,
                Path = sourceName,
                StartedAt = start,
                CompletedAt = failedTime,
                NodeCount = 0,
                EdgeCount = 0,
                Message = ex.Message
            };
        }
    }
}
