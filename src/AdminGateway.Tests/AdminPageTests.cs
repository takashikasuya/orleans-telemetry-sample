using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AdminGateway.Models;
using AdminGateway.Pages;
using AdminGateway.Services;
using Bunit;
using Grains.Abstractions;
using System.Reflection;
using Bunit.JSInterop;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    public void ShowsControlRoutingMappings()
    {
        var metrics = CreateMetricsService(
            tenants: new[] { "t1" },
            idsByType: new Dictionary<GraphNodeType, IReadOnlyList<string>>(),
            snapshots: new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase));

        ConfigureServices(metrics);

        var cut = RenderComponent<Admin>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Control Routing", cut.Markup);
            Assert.Contains("Routing Config JSON", cut.Markup);
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
    public void TrendRange_Includes24HoursOption()
    {
        var field = typeof(Admin).GetField("TrendRangeOptions", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TrendRangeOptions field was not found.");
        var options = (System.Collections.IEnumerable)(field.GetValue(null)
            ?? throw new InvalidOperationException("TrendRangeOptions value was null."));

        var labels = new List<string>();
        foreach (var option in options)
        {
            var label = option?.GetType().GetProperty("Label", BindingFlags.Public | BindingFlags.Instance)?.GetValue(option) as string;
            if (!string.IsNullOrWhiteSpace(label))
            {
                labels.Add(label);
            }
        }

        Assert.Contains("Last 24 hours", labels);
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

    [Fact]
    public async Task OnPointUpdate_ParsesNumericValuesIncludingJsonElement()
    {
        var metrics = CreateMetricsService(
            tenants: new[] { "t1" },
            idsByType: new Dictionary<GraphNodeType, IReadOnlyList<string>>(),
            snapshots: new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase));

        ConfigureServices(metrics);
        var cut = RenderComponent<Admin>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading dashboard data...", cut.Markup));

        var t0 = new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero);

        await cut.InvokeAsync(() => cut.Instance.OnPointUpdate(new Admin.PointUpdateDto
        {
            Timestamp = t0,
            Value = 12.5,
            PointId = "p1",
            DeviceId = "d1",
            TenantId = "t1"
        }));

        using var numberDoc = JsonDocument.Parse("23.75");
        await cut.InvokeAsync(() => cut.Instance.OnPointUpdate(new Admin.PointUpdateDto
        {
            Timestamp = t0.AddSeconds(1),
            Value = numberDoc.RootElement.Clone(),
            PointId = "p1",
            DeviceId = "d1",
            TenantId = "t1"
        }));

        using var numericStringDoc = JsonDocument.Parse("\"34.125\"");
        await cut.InvokeAsync(() => cut.Instance.OnPointUpdate(new Admin.PointUpdateDto
        {
            Timestamp = t0.AddSeconds(2),
            Value = numericStringDoc.RootElement.Clone(),
            PointId = "p1",
            DeviceId = "d1",
            TenantId = "t1"
        }));

        using var nonNumericStringDoc = JsonDocument.Parse("\"not-a-number\"");
        await cut.InvokeAsync(() => cut.Instance.OnPointUpdate(new Admin.PointUpdateDto
        {
            Timestamp = t0.AddSeconds(3),
            Value = nonNumericStringDoc.RootElement.Clone(),
            PointId = "p1",
            DeviceId = "d1",
            TenantId = "t1"
        }));

        var samples = GetPrivateField<IReadOnlyList<PointTrendSample>>(cut.Instance, "_pointTrendSamples");
        Assert.Equal(4, samples.Count);
        Assert.Equal(12.5, samples[0].Value);
        Assert.Equal(23.75, samples[1].Value);
        Assert.Equal(34.125, samples[2].Value);
        Assert.Null(samples[3].Value);
    }

    [Fact]
    public async Task OnPointUpdate_KeepsOnlyLatest500Samples()
    {
        var metrics = CreateMetricsService(
            tenants: new[] { "t1" },
            idsByType: new Dictionary<GraphNodeType, IReadOnlyList<string>>(),
            snapshots: new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase));

        ConfigureServices(metrics);
        var cut = RenderComponent<Admin>();

        var start = new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 501; i++)
        {
            var value = i;
            await cut.InvokeAsync(() => cut.Instance.OnPointUpdate(new Admin.PointUpdateDto
            {
                Timestamp = start.AddSeconds(value),
                Value = value,
                PointId = "p1",
                DeviceId = "d1",
                TenantId = "t1"
            }));
        }

        var samples = GetPrivateField<IReadOnlyList<PointTrendSample>>(cut.Instance, "_pointTrendSamples");
        Assert.Equal(500, samples.Count);
        Assert.Equal(start.AddSeconds(1), samples[0].Timestamp);
        Assert.Equal(1, samples[0].Value);
        Assert.Equal(start.AddSeconds(500), samples[^1].Timestamp);
        Assert.Equal(500, samples[^1].Value);
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

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field '{fieldName}' was not found.");
        return (T)(field.GetValue(target)
            ?? throw new InvalidOperationException($"Field '{fieldName}' value was null."));
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
        var storageQuery = new Mock<ITelemetryStorageQuery>();
        storageQuery
            .Setup(q => q.QueryAsync(It.IsAny<TelemetryQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TelemetryQueryResult>());

        var tempDir = Path.Combine(Path.GetTempPath(), "admin-gateway-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var routingPath = Path.Combine(tempDir, "control-routing.json");
        File.WriteAllText(routingPath, "{\"ControlRouting\":{\"DefaultConnector\":\"RabbitMq\",\"ConnectorGatewayMappings\":[{\"Connector\":\"RabbitMq\",\"GatewayIds\":[\"gw-1\"]}]}}");

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
            Options.Create(new TelemetryIngestOptions { Enabled = new[] { "RabbitMq" } }),
            configuration,
            environment,
            NullLogger<AdminMetricsService>.Instance);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "AdminGateway.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Directory.GetCurrentDirectory());
    }
}
