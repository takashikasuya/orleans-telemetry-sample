using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdminGateway.Models;
using Grains.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Telemetry.Ingest;

namespace AdminGateway.Services;

internal sealed class AdminMetricsService
{
    private readonly IClusterClient _client;
    private readonly TelemetryStorageScanner _storageScanner;
    private readonly TelemetryIngestOptions _ingestOptions;
    private readonly ILogger<AdminMetricsService> _logger;

    public AdminMetricsService(
        IClusterClient client,
        TelemetryStorageScanner storageScanner,
        IOptions<TelemetryIngestOptions> ingestOptions,
        ILogger<AdminMetricsService> logger)
    {
        _client = client;
        _storageScanner = storageScanner;
        _ingestOptions = ingestOptions.Value;
        _logger = logger;
    }

    public async Task<GrainActivationSummary[]> GetGrainActivationsAsync()
    {
        try
        {
            var mgmt = _client.GetGrain<IManagementGrain>(0);
            var stats = await mgmt.GetSimpleGrainStatistics();
            return stats
                .GroupBy(stat => string.IsNullOrWhiteSpace(stat.GrainType) ? "<unknown>" : stat.GrainType, StringComparer.OrdinalIgnoreCase)
                .Select(group => new GrainActivationSummary(
                    group.Key,
                    group.Sum(stat => stat.ActivationCount),
                    group.Select(stat => stat.SiloAddress?.ToString() ?? "unknown").Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
                .OrderByDescending(summary => summary.ActivationCount)
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to retrieve grain activation statistics.");
            return Array.Empty<GrainActivationSummary>();
        }
    }

    public async Task<SiloSummary[]> GetSiloSummariesAsync()
    {
        var mgmt = _client.GetGrain<IManagementGrain>(0);
        Dictionary<SiloAddress, SiloStatus>? hosts = null;
        SiloRuntimeStatistics[] runtimeStats = Array.Empty<SiloRuntimeStatistics>();

        try
        {
            hosts = await mgmt.GetHosts(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list silo hosts.");
        }

        var addresses = hosts?.Keys.ToArray() ?? Array.Empty<SiloAddress>();

        try
        {
            if (addresses.Length > 0)
            {
                runtimeStats = await mgmt.GetRuntimeStatistics(addresses);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to gather silo runtime statistics.");
        }

        var summaries = new List<SiloSummary>();
        for (var i = 0; i < addresses.Length; i++)
        {
            var address = addresses[i];
            var stats = i < runtimeStats.Length ? runtimeStats[i] : null;
            var status = hosts?.GetValueOrDefault(address) ?? SiloStatus.None;
            summaries.Add(CreateSummary(address.ToString() ?? "unknown", status, stats));
        }

        if (summaries.Count == 0 && runtimeStats.Length > 0)
        {
            foreach (var stats in runtimeStats)
            {
                summaries.Add(CreateSummary("unknown", SiloStatus.None, stats));
            }
        }

        return summaries.ToArray();
    }

    public Task<StorageOverview> GetStorageOverviewAsync(CancellationToken cancellationToken) =>
        _storageScanner.ScanAsync(cancellationToken);

    public IngestSummary GetIngestSummary()
    {
        var connectors = _ingestOptions.Enabled ?? Array.Empty<string>();
        var sinks = _ingestOptions.EventSinks.Enabled ?? Array.Empty<string>();
        return new IngestSummary(connectors, sinks, _ingestOptions.BatchSize, _ingestOptions.ChannelCapacity);
    }

    private static SiloSummary CreateSummary(string address, SiloStatus status, SiloRuntimeStatistics? stats)
    {
        var env = stats?.EnvironmentStatistics;
        return new SiloSummary(
            address,
            status.ToString(),
            stats?.ClientCount ?? 0,
            stats?.ActivationCount ?? 0,
            stats?.DateTime ?? DateTime.UtcNow,
            env?.FilteredCpuUsagePercentage,
            env?.FilteredMemoryUsageBytes,
            env?.MaximumAvailableMemoryBytes);
    }

    public async Task<GraphSeedStatus?> GetLastGraphSeedStatusAsync()
    {
        try
        {
            var grain = _client.GetGrain<IGraphSeedGrain>(Guid.Empty);
            return await grain.GetLastResultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read last graph seed status.");
            return null;
        }
    }

    public async Task<GraphSeedStatus> TriggerGraphSeedAsync(GraphSeedRequest request)
    {
        try
        {
            var grain = _client.GetGrain<IGraphSeedGrain>(Guid.Empty);
            return await grain.SeedAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Graph seed request failed.");
            return new GraphSeedStatus(
                false,
                request.TenantId,
                request.RdfPath,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                0,
                0,
                ex.Message);
        }
    }

    public async Task<string[]> GetGraphTenantsAsync()
    {
        try
        {
            var registry = _client.GetGrain<IGraphTenantRegistryGrain>(0);
            var tenantIds = await registry.GetTenantIdsAsync();
            return tenantIds.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve graph tenant list.");
            return Array.Empty<string>();
        }
    }

    private static readonly GraphNodeType[] StatsNodeTypes = new[]
    {
        GraphNodeType.Site,
        GraphNodeType.Building,
        GraphNodeType.Level,
        GraphNodeType.Area,
        GraphNodeType.Equipment,
        GraphNodeType.Point
    };

    public async Task<GraphStatisticsSummary> GetGraphStatisticsAsync(string tenantId = "default")
    {
        try
        {
            var index = _client.GetGrain<IGraphIndexGrain>(tenantId);

            var siteIds = await index.GetByTypeAsync(GraphNodeType.Site);
            var buildingIds = await index.GetByTypeAsync(GraphNodeType.Building);
            var levelIds = await index.GetByTypeAsync(GraphNodeType.Level);
            var areaIds = await index.GetByTypeAsync(GraphNodeType.Area);
            var equipmentIds = await index.GetByTypeAsync(GraphNodeType.Equipment);
            var pointIds = await index.GetByTypeAsync(GraphNodeType.Point);
            var idsByType = new Dictionary<GraphNodeType, IReadOnlyList<string>>
            {
                [GraphNodeType.Site] = siteIds,
                [GraphNodeType.Building] = buildingIds,
                [GraphNodeType.Level] = levelIds,
                [GraphNodeType.Area] = areaIds,
                [GraphNodeType.Equipment] = equipmentIds,
                [GraphNodeType.Point] = pointIds
            };

            var nodeSamples = await BuildNodeSamplesAsync(tenantId, idsByType);

            return new GraphStatisticsSummary(
                siteIds.Count,
                buildingIds.Count,
                levelIds.Count,
                areaIds.Count,
                equipmentIds.Count,
                pointIds.Count,
                tenantId,
                nodeSamples);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve graph statistics for tenant {TenantId}.", tenantId);
            return new GraphStatisticsSummary(0, 0, 0, 0, 0, 0, tenantId, new Dictionary<GraphNodeType, IReadOnlyList<GraphNodeDetail>>());
        }
    }

    private async Task<IReadOnlyDictionary<GraphNodeType, IReadOnlyList<GraphNodeDetail>>> BuildNodeSamplesAsync(
        string tenantId,
        IReadOnlyDictionary<GraphNodeType, IReadOnlyList<string>> idsByType)
    {
        var samples = new Dictionary<GraphNodeType, IReadOnlyList<GraphNodeDetail>>();
        foreach (var (nodeType, nodeIds) in idsByType)
        {
            var details = new List<GraphNodeDetail>();
            foreach (var nodeId in nodeIds.Take(3))
            {
                try
                {
                    var key = GraphNodeKey.Create(tenantId, nodeId);
                    var grain = _client.GetGrain<IGraphNodeGrain>(key);
                    var snapshot = await grain.GetAsync();
                    if (snapshot?.Node?.NodeId is not { } resolved)
                    {
                        continue;
                    }

                    var displayName = string.IsNullOrWhiteSpace(snapshot.Node.DisplayName) ? resolved : snapshot.Node.DisplayName;
                    details.Add(new GraphNodeDetail(resolved, displayName, snapshot.Node.NodeType));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Unable to fetch node sample {NodeId}", nodeId);
                }
            }

            samples[nodeType] = details;
        }

        return samples;
    }

    public async Task<GraphNodeHierarchy> GetGraphHierarchyAsync(string tenantId = "default", int maxDepth = 3)
    {
        try
        {
            var index = _client.GetGrain<IGraphIndexGrain>(tenantId);
            var buildingIds = await index.GetByTypeAsync(GraphNodeType.Building);

            var nodes = new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<(string NodeId, int Depth)>();
            const int maxNodes = 250;

            foreach (var buildingId in buildingIds.Take(10)) // Limit to first 10 buildings
            {
                queue.Enqueue((buildingId, 0));
            }

            while (queue.Count > 0 && nodes.Count < maxNodes)
            {
                var (nodeId, depth) = queue.Dequeue();
                if (nodes.ContainsKey(nodeId))
                {
                    continue;
                }

                var key = GraphNodeKey.Create(tenantId, nodeId);
                var grain = _client.GetGrain<IGraphNodeGrain>(key);
                var snapshot = await grain.GetAsync();
                if (snapshot?.Node?.NodeId is not { } resolvedId || string.IsNullOrWhiteSpace(resolvedId))
                {
                    continue;
                }

                nodes[resolvedId] = snapshot;

                if (depth >= maxDepth)
                {
                    continue;
                }

                foreach (var edge in snapshot.OutgoingEdges)
                {
                    if (!string.IsNullOrWhiteSpace(edge.TargetNodeId) && !nodes.ContainsKey(edge.TargetNodeId))
                    {
                        queue.Enqueue((edge.TargetNodeId, depth + 1));
                    }
                }
            }

            return new GraphNodeHierarchy(nodes.Values.ToList(), tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve graph hierarchy for tenant {TenantId}.", tenantId);
            return new GraphNodeHierarchy(new List<GraphNodeSnapshot>(), tenantId);
        }
    }
}
