using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AdminGateway.Models;
using Grains.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Telemetry.Ingest;
using Telemetry.Storage;

namespace AdminGateway.Services;

internal sealed class AdminMetricsService
{
    private readonly IClusterClient _client;
    private readonly TelemetryStorageScanner _storageScanner;
    private readonly ITelemetryStorageQuery _storageQuery;
    private readonly TelemetryIngestOptions _ingestOptions;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AdminMetricsService> _logger;

    public AdminMetricsService(
        IClusterClient client,
        TelemetryStorageScanner storageScanner,
        ITelemetryStorageQuery storageQuery,
        IOptions<TelemetryIngestOptions> ingestOptions,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<AdminMetricsService> logger)
    {
        _client = client;
        _storageScanner = storageScanner;
        _storageQuery = storageQuery;
        _ingestOptions = ingestOptions.Value;
        _configuration = configuration;
        _environment = environment;
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


    public async Task<ControlRoutingView> GetControlRoutingViewAsync(CancellationToken cancellationToken = default)
    {
        var configPath = ResolveControlRoutingConfigPath();
        var rawJson = await SafeReadAllTextAsync(configPath, cancellationToken);

        var connectorMappings = ParseConnectorMappings(rawJson)
            .Select(mapping => new ControlRoutingConnectorView(
                mapping.Connector,
                mapping.GatewayIds,
                IsConnectorEnabled(mapping.Connector)))
            .OrderBy(mapping => mapping.Connector, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var defaultConnector = ParseDefaultConnector(rawJson);

        return new ControlRoutingView(configPath, defaultConnector, connectorMappings, rawJson);
    }

    public async Task<ControlRoutingView> SaveControlRoutingConfigAsync(string rawJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw new InvalidOperationException("Control routing JSON cannot be empty.");
        }

        // Validate JSON structure before writing.
        _ = JsonDocument.Parse(rawJson);

        var configPath = ResolveControlRoutingConfigPath();
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(configPath, rawJson, cancellationToken);
        return await GetControlRoutingViewAsync(cancellationToken);
    }

    private bool IsConnectorEnabled(string connector)
    {
        if (string.IsNullOrWhiteSpace(connector))
        {
            return false;
        }

        return (_ingestOptions.Enabled ?? Array.Empty<string>())
            .Any(enabled => string.Equals(enabled, connector, StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveControlRoutingConfigPath()
    {
        var configured = _configuration["ControlRouting:ConfigPath"];
        var path = string.IsNullOrWhiteSpace(configured) ? "config/control-routing.json" : configured;
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(path, _environment.ContentRootPath);
    }

    private static async Task<string> SafeReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return "{}";
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private static string? ParseDefaultConnector(string rawJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (!document.RootElement.TryGetProperty("ControlRouting", out var controlRouting))
            {
                return null;
            }

            if (!controlRouting.TryGetProperty("DefaultConnector", out var defaultConnectorElement))
            {
                return null;
            }

            var value = defaultConnectorElement.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<ParsedConnectorMapping> ParseConnectorMappings(string rawJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (!document.RootElement.TryGetProperty("ControlRouting", out var controlRouting)
                || !controlRouting.TryGetProperty("ConnectorGatewayMappings", out var mappings)
                || mappings.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ParsedConnectorMapping>();
            }

            var results = new List<ParsedConnectorMapping>();
            foreach (var mapping in mappings.EnumerateArray())
            {
                if (!mapping.TryGetProperty("Connector", out var connectorElement))
                {
                    continue;
                }

                var connector = connectorElement.GetString();
                if (string.IsNullOrWhiteSpace(connector))
                {
                    continue;
                }

                var gatewayIds = new List<string>();
                if (mapping.TryGetProperty("GatewayIds", out var gatewayIdsElement)
                    && gatewayIdsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var gateway in gatewayIdsElement.EnumerateArray())
                    {
                        var gatewayId = gateway.GetString();
                        if (!string.IsNullOrWhiteSpace(gatewayId))
                        {
                            gatewayIds.Add(gatewayId);
                        }
                    }
                }

                results.Add(new ParsedConnectorMapping(connector, gatewayIds));
            }

            return results;
        }
        catch
        {
            return Array.Empty<ParsedConnectorMapping>();
        }
    }

    private sealed record ParsedConnectorMapping(string Connector, IReadOnlyList<string> GatewayIds);

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

    public async Task<IReadOnlyList<GraphTreeNode>> GetGraphTreeAsync(string tenantId = "default", int maxPerType = 200)
    {
        try
        {
            var snapshots = await LoadGraphSnapshotsAsync(tenantId, maxPerType);
            if (snapshots.Count == 0)
            {
                return Array.Empty<GraphTreeNode>();
            }

            var relations = BuildGraphRelations(snapshots);
            var nodeMap = snapshots
                .Where(kvp => kvp.Value.Node?.NodeId is { } id && !string.IsNullOrWhiteSpace(id))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            var childrenByParent = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var parentByChild = new Dictionary<string, (string ParentId, int Priority)>(StringComparer.OrdinalIgnoreCase);

            foreach (var relation in relations)
            {
                if (!nodeMap.ContainsKey(relation.ParentId) || !nodeMap.ContainsKey(relation.ChildId))
                {
                    continue;
                }

                if (parentByChild.TryGetValue(relation.ChildId, out var existing) && existing.Priority <= relation.Priority)
                {
                    continue;
                }

                parentByChild[relation.ChildId] = (relation.ParentId, relation.Priority);
            }

            foreach (var (childId, info) in parentByChild)
            {
                if (!childrenByParent.TryGetValue(info.ParentId, out var list))
                {
                    list = new List<string>();
                    childrenByParent[info.ParentId] = list;
                }

                list.Add(childId);
            }

            var rootIds = nodeMap.Values
                .Select(snapshot => snapshot.Node?.NodeId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Where(id => !parentByChild.ContainsKey(id))
                .ToList();

            if (rootIds.Count == 0)
            {
                rootIds = nodeMap.Values
                    .Where(snapshot => snapshot.Node?.NodeType is GraphNodeType.Site or GraphNodeType.Building)
                    .Select(snapshot => snapshot.Node!.NodeId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            var tree = new List<GraphTreeNode>();
            foreach (var rootId in rootIds)
            {
                tree.Add(BuildTreeNode(rootId, nodeMap, childrenByParent, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
            }

            return tree;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build graph tree for tenant {TenantId}.", tenantId);
            return Array.Empty<GraphTreeNode>();
        }
    }

    public async Task<GraphNodeSnapshot?> GetGraphNodeAsync(string tenantId, string nodeId)
    {
        try
        {
            var key = GraphNodeKey.Create(tenantId, nodeId);
            var grain = _client.GetGrain<IGraphNodeGrain>(key);
            return await grain.GetAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve graph node {NodeId} for tenant {TenantId}.", nodeId, tenantId);
            return null;
        }
    }

    public async Task<GraphNodeDetailView?> GetGraphNodeDetailsAsync(string tenantId, string nodeId)
    {
        var snapshot = await GetGraphNodeAsync(tenantId, nodeId);
        if (snapshot is null)
        {
            return null;
        }

        PointSnapshot? pointSnapshot = null;
        string? pointGrainKey = null;

        if (snapshot.Node.NodeType == GraphNodeType.Point)
        {
            if (TryGetAttribute(snapshot.Node.Attributes, "PointId", out var pointIdValue))
            {
                var pointId = pointIdValue!;
                pointGrainKey = PointGrainKey.Create(tenantId, pointId);

                try
                {
                    var pointGrain = _client.GetGrain<IPointGrain>(pointGrainKey);
                    pointSnapshot = await pointGrain.GetAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to load point grain snapshot for {PointKey}.", pointGrainKey);
                }
            }
        }

        return new GraphNodeDetailView(snapshot, pointSnapshot, pointGrainKey);
    }

    public async Task<IReadOnlyList<PointTrendSample>> SamplePointTrendAsync(
        string pointGrainKey,
        int sampleCount = 12,
        int intervalMs = 1000,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pointGrainKey))
        {
            return Array.Empty<PointTrendSample>();
        }

        var results = new List<PointTrendSample>(Math.Max(1, sampleCount));
        var grain = _client.GetGrain<IPointGrain>(pointGrainKey);

        for (var i = 0; i < sampleCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = await grain.GetAsync();
            var (value, raw) = NormalizeNumericValue(snapshot.LatestValue);
            results.Add(new PointTrendSample(snapshot.UpdatedAt, value, raw));

            if (i < sampleCount - 1)
            {
                await Task.Delay(intervalMs, ct);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<PointTrendSample>> QueryPointTrendAsync(
        string tenantId,
        string deviceId,
        string pointId,
        TimeSpan duration,
        int maxSamples = 240,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(deviceId) ||
            string.IsNullOrWhiteSpace(pointId))
        {
            return Array.Empty<PointTrendSample>();
        }

        var safeDuration = duration <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : duration;
        var safeMaxSamples = Math.Max(10, maxSamples);
        var to = DateTimeOffset.UtcNow;
        var from = to - safeDuration;
        var request = new TelemetryQueryRequest(
            tenantId,
            deviceId,
            from,
            to,
            pointId,
            safeMaxSamples * 4);

        var items = await _storageQuery.QueryAsync(request, ct);
        if (items.Count == 0)
        {
            return Array.Empty<PointTrendSample>();
        }

        var ordered = items
            .OrderBy(item => item.OccurredAt)
            .Select(item =>
            {
                var (value, raw) = NormalizeValueJson(item.ValueJson);
                return new PointTrendSample(item.OccurredAt, value, raw ?? item.ValueJson);
            })
            .ToList();

        return DownsampleTrend(ordered, safeMaxSamples);
    }

    public async Task<IReadOnlyList<GrainHierarchyNode>> GetGrainHierarchyAsync(
        int maxTypesPerSilo = 20,
        int maxGrainsPerType = 50)
    {
        try
        {
            var mgmt = _client.GetGrain<IManagementGrain>(0);
            var simple = await mgmt.GetSimpleGrainStatistics();
            var typeFilter = simple?
                .Select(stat => stat.GrainType)
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();

            var detailed = typeFilter.Length > 0
                ? await mgmt.GetDetailedGrainStatistics(typeFilter, Array.Empty<SiloAddress>())
                : Array.Empty<DetailedGrainStatistic>();

            if (detailed is null || !detailed.Any())
            {
                return BuildFallbackHierarchy(simple, maxTypesPerSilo);
            }

            var siloGroups = detailed
                .GroupBy(stat => stat.SiloAddress?.ToString() ?? "unknown", StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

            var result = new List<GrainHierarchyNode>();
            foreach (var siloGroup in siloGroups)
            {
                var typeNodes = new List<GrainHierarchyNode>();
                var typeGroups = siloGroup
                    .GroupBy(stat => stat.GrainType?.ToString() ?? "<unknown>", StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(group => group.Count())
                    .Take(maxTypesPerSilo);

                foreach (var typeGroup in typeGroups)
                {
                    var grainGroups = typeGroup
                        .GroupBy(stat => stat.GrainId.ToString(), StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(group => group.Count())
                        .ToList();

                    var grainNodes = new List<GrainHierarchyNode>();
                    var displayed = 0;
                    foreach (var grainGroup in grainGroups.Take(maxGrainsPerType))
                    {
                        displayed++;
                        var grainId = grainGroup.Key;
                        var activationCount = grainGroup.Count();
                        grainNodes.Add(new GrainHierarchyNode(
                            grainId,
                            $"{grainId} ({activationCount})",
                            activationCount,
                            GrainHierarchyNodeKind.GrainId,
                            Array.Empty<GrainHierarchyNode>()));
                    }

                    var remaining = grainGroups.Count - displayed;
                    if (remaining > 0)
                    {
                        grainNodes.Add(new GrainHierarchyNode(
                            $"more:{typeGroup.Key}",
                            $"+{remaining} more...",
                            0,
                            GrainHierarchyNodeKind.Info,
                            Array.Empty<GrainHierarchyNode>()));
                    }

                    var typeCount = typeGroup.Count();
                    typeNodes.Add(new GrainHierarchyNode(
                        typeGroup.Key,
                        $"{SimplifyGrainType(typeGroup.Key)} ({typeCount})",
                        typeCount,
                        GrainHierarchyNodeKind.GrainType,
                        grainNodes));
                }

                var siloCount = siloGroup.Count();
                result.Add(new GrainHierarchyNode(
                    siloGroup.Key,
                    $"{siloGroup.Key} ({siloCount})",
                    siloCount,
                    GrainHierarchyNodeKind.Silo,
                    typeNodes));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve grain hierarchy.");
            return Array.Empty<GrainHierarchyNode>();
        }
    }

    private static IReadOnlyList<GrainHierarchyNode> BuildFallbackHierarchy(
        IReadOnlyList<SimpleGrainStatistic>? stats,
        int maxTypes)
    {
        if (stats is null || stats.Count == 0)
        {
            return Array.Empty<GrainHierarchyNode>();
        }

        var typeGroups = stats
            .GroupBy(stat => stat.GrainType ?? "<unknown>", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Sum(item => item.ActivationCount))
            .Take(maxTypes);

        var typeNodes = new List<GrainHierarchyNode>();
        foreach (var typeGroup in typeGroups)
        {
            var activationCount = typeGroup.Sum(item => item.ActivationCount);
            typeNodes.Add(new GrainHierarchyNode(
                typeGroup.Key,
                $"{SimplifyGrainType(typeGroup.Key)} ({activationCount})",
                activationCount,
                GrainHierarchyNodeKind.GrainType,
                Array.Empty<GrainHierarchyNode>()));
        }

        return new[]
        {
            new GrainHierarchyNode(
                "cluster",
                $"Cluster ({typeNodes.Sum(node => node.ActivationCount)})",
                typeNodes.Sum(node => node.ActivationCount),
                GrainHierarchyNodeKind.Silo,
                typeNodes)
        };
    }

    private static string SimplifyGrainType(string grainType)
    {
        if (string.IsNullOrWhiteSpace(grainType))
        {
            return "<unknown>";
        }

        var lastDot = grainType.LastIndexOf('.');
        return lastDot > -1 && lastDot < grainType.Length - 1 ? grainType[(lastDot + 1)..] : grainType;
    }

    private async Task<Dictionary<string, GraphNodeSnapshot>> LoadGraphSnapshotsAsync(string tenantId, int maxPerType)
    {
        var index = _client.GetGrain<IGraphIndexGrain>(tenantId);
        var idsByType = new Dictionary<GraphNodeType, IReadOnlyList<string>>
        {
            [GraphNodeType.Site] = await index.GetByTypeAsync(GraphNodeType.Site),
            [GraphNodeType.Building] = await index.GetByTypeAsync(GraphNodeType.Building),
            [GraphNodeType.Level] = await index.GetByTypeAsync(GraphNodeType.Level),
            [GraphNodeType.Area] = await index.GetByTypeAsync(GraphNodeType.Area),
            [GraphNodeType.Equipment] = await index.GetByTypeAsync(GraphNodeType.Equipment),
            [GraphNodeType.Device] = await index.GetByTypeAsync(GraphNodeType.Device),
            [GraphNodeType.Point] = await index.GetByTypeAsync(GraphNodeType.Point)
        };

        var snapshots = new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var nodeIds in idsByType.Values)
        {
            foreach (var nodeId in nodeIds.Take(maxPerType))
            {
                if (snapshots.ContainsKey(nodeId))
                {
                    continue;
                }

                try
                {
                    var key = GraphNodeKey.Create(tenantId, nodeId);
                    var grain = _client.GetGrain<IGraphNodeGrain>(key);
                    var snapshot = await grain.GetAsync();
                    if (snapshot?.Node?.NodeId is not { } resolved || string.IsNullOrWhiteSpace(resolved))
                    {
                        continue;
                    }

                    snapshots[resolved] = snapshot;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Unable to load node {NodeId} for tree building.", nodeId);
                }
            }
        }

        return snapshots;
    }

    private static List<GraphRelation> BuildGraphRelations(Dictionary<string, GraphNodeSnapshot> snapshots)
    {
        var relations = new List<GraphRelation>();
        var containment = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hasBuilding", "hasLevel", "hasArea", "hasPart", "hasEquipment", "hasPoint"
        };

        var relationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (nodeId, snapshot) in snapshots)
        {
            var sourceType = NormalizeNodeType(snapshot.Node?.NodeType ?? GraphNodeType.Unknown);
            AddAttributeBasedRelation(snapshot.Node, nodeId, sourceType, relationKeys, relations);

            foreach (var edge in snapshot.OutgoingEdges)
            {
                if (string.IsNullOrWhiteSpace(edge.TargetNodeId))
                {
                    continue;
                }

                var targetId = edge.TargetNodeId;
                if (!snapshots.TryGetValue(targetId, out var targetSnapshot))
                {
                    continue;
                }

                var targetType = NormalizeNodeType(targetSnapshot.Node?.NodeType ?? GraphNodeType.Unknown);
                var predicate = NormalizePredicate(edge.Predicate);

                if (containment.Contains(predicate))
                {
                    if (IsContainmentPair(sourceType, targetType))
                    {
                        AddRelation(nodeId, targetId, 1, relationKeys, relations);
                    }

                    continue;
                }

                if (string.Equals(predicate, "isPartOf", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsContainmentPair(targetType, sourceType))
                    {
                        AddRelation(targetId, nodeId, 1, relationKeys, relations);
                    }

                    continue;
                }

                if (string.Equals(predicate, "isPointOf", StringComparison.OrdinalIgnoreCase))
                {
                    if (targetType == GraphNodeType.Equipment && sourceType == GraphNodeType.Point)
                    {
                        AddRelation(targetId, nodeId, 1, relationKeys, relations);
                    }

                    continue;
                }

                if (string.Equals(predicate, "locatedIn", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsLocationPair(targetType, sourceType))
                    {
                        AddRelation(targetId, nodeId, 2, relationKeys, relations);
                    }

                    continue;
                }

                if (string.Equals(predicate, "isLocationOf", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsLocationPair(sourceType, targetType))
                    {
                        AddRelation(nodeId, targetId, 2, relationKeys, relations);
                    }
                }
            }
        }

        return relations;
    }

    private static void AddRelation(
        string parentId,
        string childId,
        int priority,
        HashSet<string> relationKeys,
        List<GraphRelation> relations)
    {
        var key = $"{parentId}|{childId}|{priority}";
        if (relationKeys.Add(key))
        {
            relations.Add(new GraphRelation(parentId, childId, priority));
        }
    }

    private static void AddAttributeBasedRelation(
        GraphNodeDefinition? node,
        string nodeId,
        GraphNodeType nodeType,
        HashSet<string> relationKeys,
        List<GraphRelation> relations)
    {
        if (node is null)
        {
            return;
        }

        if (nodeType == GraphNodeType.Building && TryGetAttribute(node.Attributes, "SiteUri", out var siteUri))
        {
            AddRelation(siteUri!, nodeId, 1, relationKeys, relations);
        }
        else if (nodeType == GraphNodeType.Level && TryGetAttribute(node.Attributes, "BuildingUri", out var buildingUri))
        {
            AddRelation(buildingUri!, nodeId, 1, relationKeys, relations);
        }
        else if (nodeType == GraphNodeType.Area && TryGetAttribute(node.Attributes, "LevelUri", out var levelUri))
        {
            AddRelation(levelUri!, nodeId, 1, relationKeys, relations);
        }
        else if (nodeType == GraphNodeType.Equipment && TryGetAttribute(node.Attributes, "AreaUri", out var areaUri))
        {
            AddRelation(areaUri!, nodeId, 2, relationKeys, relations);
        }
    }

    private static string NormalizePredicate(string predicate)
    {
        if (string.IsNullOrWhiteSpace(predicate))
        {
            return string.Empty;
        }

        var hashIndex = predicate.LastIndexOf('#');
        var slashIndex = predicate.LastIndexOf('/');
        var index = Math.Max(hashIndex, slashIndex);
        return index >= 0 && index < predicate.Length - 1 ? predicate[(index + 1)..] : predicate;
    }

    private static GraphTreeNode BuildTreeNode(
        string nodeId,
        Dictionary<string, GraphNodeSnapshot> nodeMap,
        Dictionary<string, List<string>> childrenByParent,
        HashSet<string> visited)
    {
        if (!nodeMap.TryGetValue(nodeId, out var snapshot) || snapshot.Node is null)
        {
            return new GraphTreeNode(nodeId, nodeId, GraphNodeType.Unknown, Array.Empty<GraphTreeNode>());
        }

        if (!visited.Add(nodeId))
        {
            return new GraphTreeNode(nodeId, nodeId, NormalizeNodeType(snapshot.Node.NodeType), Array.Empty<GraphTreeNode>());
        }

        var displayName = string.IsNullOrWhiteSpace(snapshot.Node.DisplayName) ? snapshot.Node.NodeId : snapshot.Node.DisplayName;
        var children = new List<GraphTreeNode>();
        if (childrenByParent.TryGetValue(nodeId, out var childIds))
        {
            foreach (var childId in childIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                children.Add(BuildTreeNode(childId, nodeMap, childrenByParent, visited));
            }
        }

        visited.Remove(nodeId);
        return new GraphTreeNode(snapshot.Node.NodeId, displayName, snapshot.Node.NodeType, children);
    }

    private static GraphNodeType NormalizeNodeType(GraphNodeType nodeType)
        => nodeType == GraphNodeType.Device ? GraphNodeType.Equipment : nodeType;

    private static bool IsContainmentPair(GraphNodeType parent, GraphNodeType child)
        => (parent is GraphNodeType.Site or GraphNodeType.Building or GraphNodeType.Level or GraphNodeType.Area
            && child is GraphNodeType.Building or GraphNodeType.Level or GraphNodeType.Area or GraphNodeType.Equipment)
           || (parent == GraphNodeType.Equipment && child == GraphNodeType.Point);

    private static bool IsLocationPair(GraphNodeType areaType, GraphNodeType equipmentType)
        => areaType == GraphNodeType.Area
           && equipmentType == GraphNodeType.Equipment;

    private static bool TryGetAttribute(IReadOnlyDictionary<string, string> attributes, string key, out string? value)
    {
        if (attributes.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            return true;
        }

        value = null;
        return false;
    }

    private sealed record GraphRelation(string ParentId, string ChildId, int Priority);

    private static (double? Value, string? Raw) NormalizeNumericValue(object? value)
    {
        if (value is null)
        {
            return (null, null);
        }

        switch (value)
        {
            case double d:
                return (d, d.ToString(CultureInfo.InvariantCulture));
            case float f:
                return (f, f.ToString(CultureInfo.InvariantCulture));
            case decimal dec:
                return ((double)dec, dec.ToString(CultureInfo.InvariantCulture));
            case int i:
                return (i, i.ToString(CultureInfo.InvariantCulture));
            case long l:
                return (l, l.ToString(CultureInfo.InvariantCulture));
            case short s:
                return (s, s.ToString(CultureInfo.InvariantCulture));
            case byte b:
                return (b, b.ToString(CultureInfo.InvariantCulture));
            case bool boolean:
                return (boolean ? 1 : 0, boolean.ToString());
            case string text:
                if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                {
                    return (parsed, text);
                }
                return (null, text);
            case JsonElement element:
                return NormalizeJsonElement(element);
            default:
                return (null, value.ToString());
        }
    }

    private static (double? Value, string? Raw) NormalizeJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetDouble(out var number):
                return (number, number.ToString(CultureInfo.InvariantCulture));
            case JsonValueKind.String:
                var text = element.GetString();
                if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                {
                    return (parsed, text);
                }
                return (null, text);
            case JsonValueKind.True:
                return (1, "true");
            case JsonValueKind.False:
                return (0, "false");
            default:
                return (null, element.ToString());
        }
    }

    private static (double? Value, string? Raw) NormalizeValueJson(string? valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(valueJson);
            var normalized = NormalizeNumericValue(document.RootElement.Clone());
            return normalized.Raw is null ? (normalized.Value, valueJson) : normalized;
        }
        catch (JsonException)
        {
            if (double.TryParse(valueJson, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return (parsed, valueJson);
            }

            return (null, valueJson);
        }
    }

    private static IReadOnlyList<PointTrendSample> DownsampleTrend(IReadOnlyList<PointTrendSample> samples, int maxSamples)
    {
        if (samples.Count <= maxSamples)
        {
            return samples;
        }

        var step = (double)(samples.Count - 1) / (maxSamples - 1);
        var reduced = new List<PointTrendSample>(maxSamples);
        for (var i = 0; i < maxSamples; i++)
        {
            var index = (int)Math.Round(i * step);
            index = Math.Clamp(index, 0, samples.Count - 1);
            reduced.Add(samples[index]);
        }

        return reduced;
    }
}
