using System;
using System.Collections.Generic;
using System.IO;
using AdminGateway.Models;
using AdminGateway.Services;
using Grains.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Orleans;
using Telemetry.Ingest;
using Telemetry.Storage;

namespace AdminGateway.Tests;

/// <summary>
/// Service-layer tests for <see cref="AdminMetricsService"/> graph tree construction.
/// Covers the key "breakage points" identified in docs/admin-gateway-rdf-ui-test-strategy.md:
///   1. Containment predicates (hasBuilding/hasLevel/hasArea/hasEquipment/hasPoint)
///   2. Reverse relation (isPartOf)
///   3. Location relation (locatedIn / isLocationOf)
///   4. Device→Equipment type normalisation
///   5. Cyclic relation guard
/// </summary>
public sealed class AdminMetricsServiceTests
{
    // -------------------------------------------------------------------------
    // 1. Containment predicates build expected tree
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetGraphTree_ContainmentPredicates_BuildsFullHierarchy()
    {
        // Arrange: Site→Building→Level→Area→Equipment→Point linked by containment edges
        var snapshots = new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-1"] = MakeSnapshot("site-1", "HQ Site", GraphNodeType.Site,
                ("hasBuilding", "building-1")),
            ["building-1"] = MakeSnapshot("building-1", "Main Building", GraphNodeType.Building,
                ("hasLevel", "level-1")),
            ["level-1"] = MakeSnapshot("level-1", "Floor 1", GraphNodeType.Level,
                ("hasArea", "area-1")),
            ["area-1"] = MakeSnapshot("area-1", "Lobby", GraphNodeType.Area,
                ("hasEquipment", "equip-1")),
            ["equip-1"] = MakeSnapshot("equip-1", "AHU-1", GraphNodeType.Equipment,
                ("hasPoint", "point-1")),
            ["point-1"] = MakeSnapshot("point-1", "Supply Temp", GraphNodeType.Point)
        };

        var idsByType = new Dictionary<GraphNodeType, IReadOnlyList<string>>
        {
            [GraphNodeType.Site] = ["site-1"],
            [GraphNodeType.Building] = ["building-1"],
            [GraphNodeType.Level] = ["level-1"],
            [GraphNodeType.Area] = ["area-1"],
            [GraphNodeType.Equipment] = ["equip-1"],
            [GraphNodeType.Point] = ["point-1"]
        };

        var service = CreateService(idsByType, snapshots);

        // Act
        var tree = await service.GetGraphTreeAsync("t1");

        // Assert: single root at site level with full descendant chain
        Assert.Single(tree);
        var site = tree[0];
        Assert.Equal("site-1", site.NodeId);
        Assert.Equal("HQ Site", site.DisplayName);

        var building = Assert.Single(site.Children);
        Assert.Equal("building-1", building.NodeId);

        var level = Assert.Single(building.Children);
        Assert.Equal("level-1", level.NodeId);

        var area = Assert.Single(level.Children);
        Assert.Equal("area-1", area.NodeId);

        var equipment = Assert.Single(area.Children);
        Assert.Equal("equip-1", equipment.NodeId);

        var point = Assert.Single(equipment.Children);
        Assert.Equal("point-1", point.NodeId);
    }

    // -------------------------------------------------------------------------
    // 2. isPartOf reverse relation places child under parent
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetGraphTree_IsPartOf_ReversedRelation_PlacesNodeUnderParent()
    {
        // Arrange: Building has an isPartOf edge pointing to Site (reverse direction)
        var snapshots = new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["site-1"] = MakeSnapshot("site-1", "HQ Site", GraphNodeType.Site),
            ["building-1"] = MakeSnapshot("building-1", "Main Building", GraphNodeType.Building,
                ("isPartOf", "site-1"))
        };

        var idsByType = new Dictionary<GraphNodeType, IReadOnlyList<string>>
        {
            [GraphNodeType.Site] = ["site-1"],
            [GraphNodeType.Building] = ["building-1"]
        };

        var service = CreateService(idsByType, snapshots);

        // Act
        var tree = await service.GetGraphTreeAsync("t1");

        // Assert: site is root; building is its child via reversed relation
        var site = tree.FirstOrDefault(n => n.NodeId == "site-1");
        Assert.NotNull(site);
        var building = Assert.Single(site.Children);
        Assert.Equal("building-1", building.NodeId);
    }

    // -------------------------------------------------------------------------
    // 3. locatedIn places Equipment under Area
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetGraphTree_LocatedIn_PlacesEquipmentUnderArea()
    {
        // Arrange: Equipment uses locatedIn → Area (location relation)
        var snapshots = new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["area-1"] = MakeSnapshot("area-1", "Server Room", GraphNodeType.Area),
            ["equip-1"] = MakeSnapshot("equip-1", "CRAC-1", GraphNodeType.Equipment,
                ("locatedIn", "area-1"))
        };

        var idsByType = new Dictionary<GraphNodeType, IReadOnlyList<string>>
        {
            [GraphNodeType.Area] = ["area-1"],
            [GraphNodeType.Equipment] = ["equip-1"]
        };

        var service = CreateService(idsByType, snapshots);

        // Act
        var tree = await service.GetGraphTreeAsync("t1");

        // Assert: area is root (no parent); equipment is area's child
        var area = tree.FirstOrDefault(n => n.NodeId == "area-1");
        Assert.NotNull(area);
        var equip = Assert.Single(area.Children);
        Assert.Equal("equip-1", equip.NodeId);
    }

    // -------------------------------------------------------------------------
    // 4. Device type is normalised to Equipment in tree output
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetGraphTree_DeviceType_IsNormalisedToEquipment()
    {
        // Arrange: a node with NodeType=Device
        var snapshots = new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["device-1"] = MakeSnapshot("device-1", "Sensor A", GraphNodeType.Device)
        };

        var idsByType = new Dictionary<GraphNodeType, IReadOnlyList<string>>
        {
            [GraphNodeType.Device] = ["device-1"]
        };

        var service = CreateService(idsByType, snapshots);

        // Act
        var tree = await service.GetGraphTreeAsync("t1");

        // Assert: the returned tree node uses Equipment, not Device
        var node = tree.FirstOrDefault(n => n.NodeId == "device-1");
        Assert.NotNull(node);
        Assert.Equal(GraphNodeType.Equipment, node.NodeType);
    }

    // -------------------------------------------------------------------------
    // 5. Cyclic relation does not cause infinite recursion / stack overflow
    // -------------------------------------------------------------------------
    [Fact]
    public async Task GetGraphTree_CyclicRelation_DoesNotHang()
    {
        // Arrange: Equipment A → hasPoint → Point B, and Point B → hasPart → Equipment A  (cycle)
        var snapshots = new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["equip-1"] = MakeSnapshot("equip-1", "AHU-1", GraphNodeType.Equipment,
                ("hasPoint", "point-1")),
            ["point-1"] = MakeSnapshot("point-1", "Sensor", GraphNodeType.Point,
                ("hasPart", "equip-1"))   // back-edge creates cycle in edge data
        };

        var idsByType = new Dictionary<GraphNodeType, IReadOnlyList<string>>
        {
            [GraphNodeType.Equipment] = ["equip-1"],
            [GraphNodeType.Point] = ["point-1"]
        };

        var service = CreateService(idsByType, snapshots);

        // Act: must complete without StackOverflowException
        var tree = await service.GetGraphTreeAsync("t1");

        // Assert: result is returned; both nodes accounted for
        Assert.NotEmpty(tree);
        var allIds = Collect(tree);
        Assert.Contains("equip-1", allIds);
        Assert.Contains("point-1", allIds);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Collects all node IDs in a depth-first traversal.</summary>
    private static IEnumerable<string> Collect(IEnumerable<GraphTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node.NodeId;
            foreach (var id in Collect(node.Children))
            {
                yield return id;
            }
        }
    }

    private static GraphNodeSnapshot MakeSnapshot(
        string id,
        string displayName,
        GraphNodeType type,
        params (string Predicate, string TargetNodeId)[] edges)
    {
        var snapshot = new GraphNodeSnapshot
        {
            Node = new GraphNodeDefinition
            {
                NodeId = id,
                DisplayName = displayName,
                NodeType = type
            }
        };

        foreach (var (predicate, targetId) in edges)
        {
            snapshot.OutgoingEdges.Add(new GraphEdge { Predicate = predicate, TargetNodeId = targetId });
        }

        return snapshot;
    }

    private static AdminMetricsService CreateService(
        IReadOnlyDictionary<GraphNodeType, IReadOnlyList<string>> idsByType,
        IReadOnlyDictionary<string, GraphNodeSnapshot> snapshots)
    {
        var index = new Mock<IGraphIndexGrain>();
        index.Setup(x => x.GetByTypeAsync(It.IsAny<GraphNodeType>()))
            .ReturnsAsync((GraphNodeType t) => idsByType.TryGetValue(t, out var ids)
                ? ids
                : Array.Empty<string>());

        var client = new Mock<IClusterClient>();
        client
            .Setup(c => c.GetGrain<IGraphIndexGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(index.Object);

        client
            .Setup(c => c.GetGrain<IGraphNodeGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((key, _) =>
            {
                // key format produced by GraphNodeKey.Create is "tenantId:nodeId"
                var nodeId = key.Contains(':') ? key[(key.IndexOf(':') + 1)..] : key;
                var grain = new Mock<IGraphNodeGrain>();
                grain.Setup(g => g.GetAsync()).ReturnsAsync(
                    snapshots.TryGetValue(nodeId, out var snap)
                        ? snap
                        : new GraphNodeSnapshot
                        {
                            Node = new GraphNodeDefinition
                            {
                                NodeId = nodeId,
                                DisplayName = nodeId,
                                NodeType = GraphNodeType.Unknown
                            }
                        });
                return grain.Object;
            });

        var storageScanner = new TelemetryStorageScanner(
            Options.Create(new TelemetryStorageOptions
            {
                StagePath = "./not-used-stage",
                ParquetPath = "./not-used-parquet",
                IndexPath = "./not-used-index"
            }),
            NullLogger<TelemetryStorageScanner>.Instance);

        var storageQuery = new Mock<ITelemetryStorageQuery>();
        storageQuery
            .Setup(q => q.QueryAsync(It.IsAny<TelemetryQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TelemetryQueryResult>());

        var tempDir = Path.Combine(Path.GetTempPath(), "admin-metrics-svc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var routingPath = Path.Combine(tempDir, "control-routing.json");
        File.WriteAllText(routingPath,
            "{\"ControlRouting\":{\"DefaultConnector\":\"RabbitMq\",\"ConnectorGatewayMappings\":[]}}");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ControlRouting:ConfigPath"] = routingPath
            })
            .Build();

        var environment = new TestHostEnvironment { ContentRootPath = tempDir };

        return new AdminMetricsService(
            client.Object,
            storageScanner,
            storageQuery.Object,
            Options.Create(new TelemetryIngestOptions { Enabled = ["RabbitMq"] }),
            configuration,
            environment,
            NullLogger<AdminMetricsService>.Instance);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "AdminGateway.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Directory.GetCurrentDirectory());
    }
}
