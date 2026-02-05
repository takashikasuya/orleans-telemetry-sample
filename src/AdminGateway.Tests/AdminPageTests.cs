using System;
using System.Collections.Generic;
using System.Linq;
using AdminGateway.Models;
using AdminGateway.Pages;
using AdminGateway.Services;
using Bunit;
using Grains.Abstractions;
using System.Reflection;
using Bunit.JSInterop;
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
                [GraphNodeType.Equipment] = new[] { "equip-1" }
            },
            snapshots: new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["site-1"] = Snapshot("site-1", "HQ Site", GraphNodeType.Site,
                    new GraphEdge { Predicate = "hasEquipment", TargetNodeId = "equip-1" }),
                ["equip-1"] = Snapshot("equip-1", "AHU-1", GraphNodeType.Equipment)
            });

        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(metrics);

        var cut = RenderComponent<Admin>();

        ClickButton(cut, "Load Hierarchy");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Hierarchy Tree", cut.Markup);
            Assert.Contains("HQ Site", cut.Markup);
            Assert.Contains("AHU-1", cut.Markup);
        });
    }

    [Fact]
    public async Task SelectingTreeNode_ShowsNormalizedClassAndAttributes()
    {
        var metrics = CreateMetricsService(
            tenants: new[] { "t1" },
            idsByType: new Dictionary<GraphNodeType, IReadOnlyList<string>>
            {
                [GraphNodeType.Device] = new[] { "device-01" }
            },
            snapshots: new Dictionary<string, GraphNodeSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["device-01"] = new GraphNodeSnapshot
                {
                    Node = new GraphNodeDefinition
                    {
                        NodeId = "device-01",
                        DisplayName = "AHU Device",
                        NodeType = GraphNodeType.Device,
                        Attributes = new Dictionary<string, string>
                        {
                            ["manufacturer"] = "Contoso"
                        }
                    }
                }
            });

        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(metrics);

        var cut = RenderComponent<Admin>();

        ClickButton(cut, "Load Hierarchy");
        cut.WaitForAssertion(() => Assert.Contains("AHU Device", cut.Markup));

        await cut.InvokeAsync(() => InvokePrivateAsync(cut.Instance, "SelectGraphNodeAsync", "device-01"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("ID:</strong> device-01", cut.Markup);
            Assert.Contains("Class:</strong> Equipment", cut.Markup);
            Assert.Contains("manufacturer: Contoso", cut.Markup);
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
        IReadOnlyDictionary<string, GraphNodeSnapshot> snapshots)
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
