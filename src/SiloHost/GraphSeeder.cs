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
            var index = _grainFactory.GetGrain<IGraphIndexGrain>(tenantId);

            foreach (var node in seed.Nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = GraphNodeKey.Create(tenantId, node.NodeId);
                var grain = _grainFactory.GetGrain<IGraphNodeGrain>(key);
                await grain.UpsertAsync(node);
                await index.AddNodeAsync(node);
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
            _logger.LogInformation("Graph seed completed. Path={Path} Tenant={Tenant} Nodes={NodeCount} Edges={EdgeCount}",
                path,
                tenantId,
                seed.Nodes.Count,
                seed.Edges.Count);

            return new GraphSeedStatus
            {
                Success = true,
                TenantId = tenantId,
                Path = path,
                StartedAt = start,
                CompletedAt = completed,
                NodeCount = seed.Nodes.Count,
                EdgeCount = seed.Edges.Count
            };
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
}
