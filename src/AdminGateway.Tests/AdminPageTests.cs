using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AdminGateway.Models;
using AdminGateway.Pages;
using AdminGateway.Services;
using Bunit;
using Grains.Abstractions;
using System.Reflection;
using Bunit.JSInterop;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MudBlazor.Services;
using Orleans;
using Telemetry.Ingest;
using Telemetry.Storage;

namespace AdminGateway.Tests;

public sealed class AdminPageTests : TestContext
{
    [Fact]
    public void LoadHierarchy_ShowsTreePanelAndNodeLabel()
    {
        var metrics = CreateMetricsService(
            tenants: new[] { "t1" },
            idsByType: new Dictionary<GraphNodeType, IReadOnlyList<string>>
            {
                [GraphNodeType.Site] = new[] { "site-1" },
                [GraphNodeType.Building] = new[] { "building-1" },
                [GraphNodeType.Area] = new[] { "area-1" },
                [GraphNodeType.Equipment] = new[] { "equip-1" },
                [GraphNodeType.Point] = new[] { "point-1" }
            },
            snapshots: new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["site-1"] = Snapshot("site-1", "HQ Site", GraphNodeType.Site,
                    new GraphEdge { Predicate = "hasBuilding", TargetNodeId = "building-1" }),
                ["building-1"] = Snapshot("building-1", "Main Building", GraphNodeType.Building,
                    new GraphEdge { Predicate = "hasArea", TargetNodeId = "area-1" }),
                ["area-1"] = Snapshot("area-1", "Lobby", GraphNodeType.Area,
                    new GraphEdge { Predicate = "hasEquipment", TargetNodeId = "equip-1" }),
                ["equip-1"] = Snapshot("equip-1", "AHU-1", GraphNodeType.Equipment,
                    new GraphEdge { Predicate = "hasPoint", TargetNodeId = "point-1" }),
                ["point-1"] = Snapshot("point-1", "Supply Temp", GraphNodeType.Point)
            });

        ConfigureServices(metrics);

        var cut = RenderComponent<Admin>();

        ClickButton(cut, "Load Hierarchy");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Hierarchy Tree", cut.Markup);
            Assert.Contains("HQ Site", cut.Markup);
            Assert.Contains("AHU-1", cut.Markup);
            Assert.Contains("Supply Temp", cut.Markup);
        });
    }

    [Fact]
    public void GraphImport_ShowsFilePicker()
    {
        var metrics = CreateMetricsService(
            tenants: new[] { "t1" },
            idsByType: new Dictionary<GraphNodeType, IReadOnlyList<string>>(),
            snapshots: new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase));

        ConfigureServices(metrics);

        var cut = RenderComponent<Admin>();

        cut.WaitForAssertion(() =>
        {
            var input = cut.Find("input[type=file]");
            Assert.NotNull(input);
        });

        Assert.Contains("RDF file", cut.Markup);
    }

    [Fact]
    public async Task SelectingPointNode_ShowsMetadataAndPointSnapshot()
    {
        var pointNode = Snapshot("point-1", "Supply Temp", GraphNodeType.Point);
        pointNode.Node.Attributes["PointId"] = "pt-1";
        pointNode.Node.Attributes["DeviceId"] = "device-1";
        pointNode.Node.Attributes["BuildingName"] = "building";
        pointNode.Node.Attributes["SpaceId"] = "space";
        pointNode.Node.Attributes["Unit"] = "C";
        pointNode.OutgoingEdges.Add(new GraphEdge { Predicate = "isPointOf", TargetNodeId = "equip-1" });

        var pointKey = PointGrainKey.Create("t1", "pt-1");

        var metrics = CreateMetricsService(
            tenants: new[] { "t1" },
            idsByType: new Dictionary<GraphNodeType, IReadOnlyList<string>>
            {
                [GraphNodeType.Point] = new[] { "point-1" }
            },
            snapshots: new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["point-1"] = pointNode
            },
            pointSnapshots: new Dictionary<string, PointSnapshot>
            {
                [pointKey] = new PointSnapshot(12, 22.5, new DateTimeOffset(2026, 2, 6, 1, 2, 3, TimeSpan.Zero))
            });

        ConfigureServices(metrics);

        var cut = RenderComponent<Admin>();

        ClickButton(cut, "Load Hierarchy");
        cut.WaitForAssertion(() => Assert.Contains("Supply Temp", cut.Markup));

        await cut.InvokeAsync(() => InvokePrivateAsync(cut.Instance, "SelectGraphNodeAsync", "point-1"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("<th>ID</th>", cut.Markup);
            Assert.Contains("<td>point-1</td>", cut.Markup);
            Assert.Contains("<th>Unit</th>", cut.Markup);
            Assert.Contains("<td>C</td>", cut.Markup);
            Assert.Contains("Point Snapshot", cut.Markup);
            Assert.Contains("<th>Sequence</th>", cut.Markup);
            Assert.Contains("<td>12</td>", cut.Markup);
            Assert.Contains("<th>Value</th>", cut.Markup);
            Assert.Contains("<td>22.5</td>", cut.Markup);
        });
    }

    private static Task InvokePrivateAsync(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found.");

        return (Task)(method.Invoke(target, args)
            ?? throw new InvalidOperationException($"Method '{methodName}' returned null."));
    }

    private static void ClickButton(IRenderedComponent<Admin> cut, string text)
    {
        cut.WaitForAssertion(() =>
        {
            var exists = cut.FindAll("button")
                .Any(b => b.TextContent.Contains(text, StringComparison.Ordinal));
            Assert.True(exists, $"Button '{text}' was not rendered yet.");
        });

        var button = cut.FindAll("button")
            .First(b => b.TextContent.Contains(text, StringComparison.Ordinal));
        button.Click();
    }

    private void ConfigureServices(AdminMetricsService metrics)
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(metrics);
        Services.AddSingleton<IConfiguration>(BuildConfiguration());
    }

    private static IConfiguration BuildConfiguration()
    {
        var values = new Dictionary<string, string?>
        {
            ["ADMIN_GRAPH_UPLOAD_DIR"] = Path.Combine(Path.GetTempPath(), "orleans-telemetry-test-uploads"),
            ["ADMIN_GRAPH_UPLOAD_MAX_BYTES"] = "1048576"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static GraphNodeSnapshot Snapshot(string id, string displayName, GraphNodeType type, params GraphEdge[] outgoingEdges)
        => new()
        {
            Node = new GraphNodeDefinition
            {
                NodeId = id,
                DisplayName = displayName,
                NodeType = type
            },
            OutgoingEdges = outgoingEdges.ToList()
        };

    private static AdminMetricsService CreateMetricsService(
        IReadOnlyList<string> tenants,
        IReadOnlyDictionary<GraphNodeType, IReadOnlyList<string>> idsByType,
        IReadOnlyDictionary<string, GraphNodeSnapshot> snapshots,
        IReadOnlyDictionary<string, PointSnapshot>? pointSnapshots = null)
    {
        var registry = new Mock<IGraphTenantRegistryGrain>();
        registry.Setup(x => x.GetTenantIdsAsync()).ReturnsAsync(tenants);

        var index = new Mock<IGraphIndexGrain>();
        index.Setup(x => x.GetByTypeAsync(It.IsAny<GraphNodeType>()))
            .ReturnsAsync((GraphNodeType type) => idsByType.TryGetValue(type, out var ids)
                ? ids
                : Array.Empty<string>());

        var client = new Mock<IClusterClient>();
        client
            .Setup(c => c.GetGrain<IGraphTenantRegistryGrain>(It.IsAny<long>(), It.IsAny<string?>()))
            .Returns(registry.Object);
        client
            .Setup(c => c.GetGrain<IGraphIndexGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(index.Object);
        client
            .Setup(c => c.GetGrain<IGraphNodeGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((key, _) =>
            {
                var nodeId = key.Contains(':') ? key[(key.IndexOf(':') + 1)..] : key;
                var grain = new Mock<IGraphNodeGrain>();
                grain.Setup(g => g.GetAsync()).ReturnsAsync(
                    snapshots.TryGetValue(nodeId, out var snapshot)
                        ? snapshot
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

        var pointGrain = new Mock<IPointGrain>();
        pointGrain.Setup(g => g.GetAsync()).ReturnsAsync(new PointSnapshot(0, null, DateTimeOffset.MinValue));

        client
            .Setup(c => c.GetGrain<IPointGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((key, _) =>
            {
                var grain = new Mock<IPointGrain>();
                if (pointSnapshots is not null && pointSnapshots.TryGetValue(key, out var snapshot))
                {
                    grain.Setup(g => g.GetAsync()).ReturnsAsync(snapshot);
                }
                else
                {
                    grain.Setup(g => g.GetAsync()).ReturnsAsync(new PointSnapshot(0, null, DateTimeOffset.MinValue));
                }
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

        return new AdminMetricsService(
            client.Object,
            storageScanner,
            Options.Create(new TelemetryIngestOptions()),
            NullLogger<AdminMetricsService>.Instance);
    }
}
