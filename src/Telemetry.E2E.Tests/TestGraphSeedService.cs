using DataModel.Analyzer.Integration;
using Grains.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Telemetry.E2E.Tests;

internal sealed class TestGraphSeedService : BackgroundService
{
    private readonly OrleansIntegrationService _integration;
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<TestGraphSeedService> _logger;

    public TestGraphSeedService(
        OrleansIntegrationService integration,
        IGrainFactory grainFactory,
        ILogger<TestGraphSeedService> logger)
    {
        _integration = integration;
        _grainFactory = grainFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seedPath = Environment.GetEnvironmentVariable("RDF_SEED_PATH");
        if (string.IsNullOrWhiteSpace(seedPath))
        {
            _logger.LogInformation("RDF_SEED_PATH is not set. Skipping graph seed.");
            return;
        }

        var tenant = Environment.GetEnvironmentVariable("TENANT_ID") ?? "default";
        _logger.LogInformation("Seeding graph from RDF: {Path} (tenant: {Tenant})", seedPath, tenant);

        var seed = await _integration.ExtractGraphSeedDataAsync(seedPath);
        var index = _grainFactory.GetGrain<IGraphIndexGrain>(tenant);

        foreach (var node in seed.Nodes)
        {
            var key = GraphNodeKey.Create(tenant, node.NodeId);
            var grain = _grainFactory.GetGrain<IGraphNodeGrain>(key);
            await grain.UpsertAsync(node);
            await index.AddNodeAsync(node);
        }

        foreach (var edge in seed.Edges)
        {
            var sourceKey = GraphNodeKey.Create(tenant, edge.SourceNodeId);
            var targetKey = GraphNodeKey.Create(tenant, edge.TargetNodeId);
            var source = _grainFactory.GetGrain<IGraphNodeGrain>(sourceKey);
            var target = _grainFactory.GetGrain<IGraphNodeGrain>(targetKey);
            var outgoing = new GraphEdge { Predicate = edge.Predicate, TargetNodeId = edge.TargetNodeId };
            var incoming = new GraphEdge { Predicate = edge.Predicate, TargetNodeId = edge.SourceNodeId };
            await source.AddOutgoingEdgeAsync(outgoing);
            await target.AddIncomingEdgeAsync(incoming);
        }

        _logger.LogInformation("Graph seed completed. Nodes={NodeCount}, Edges={EdgeCount}", seed.Nodes.Count, seed.Edges.Count);
    }
}
