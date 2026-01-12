using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ApiGateway.Services;
using FluentAssertions;
using Grains.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ApiGateway.Tests;

public sealed class GraphRegistryServiceTests
{
    [Fact]
    public async Task GetNodesAsync_WhenNodeCountExceedsInlineLimit_CreatesExport()
    {
        var nodeIds = new[] { "building:1", "building:2", "building:3" };
        var registry = CreateGraphRegistry(nodeIds, GraphNodeType.Building, maxInlineRecords: 2, out var exportService, out var tempRoot);
        try
        {
            var result = await registry.GetNodesAsync("tenant-a", GraphNodeType.Building, null, CancellationToken.None);
            result.Mode.Should().Be("url");
            result.TotalCount.Should().Be(nodeIds.Length);
            result.Count.Should().Be(nodeIds.Length);
            result.Url.Should().NotBeNullOrWhiteSpace();

            var exportId = result.Url!.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
            var opened = await exportService.TryOpenExportAsync(exportId, "tenant-a", DateTimeOffset.UtcNow, CancellationToken.None);
            opened.Status.Should().Be(RegistryExportOpenStatus.Ready);
            opened.Stream.Should().NotBeNull();
            await opened.Stream!.DisposeAsync();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task GetNodesAsync_WithLimit_ReturnsInlineSubset()
    {
        var nodeIds = new[] { "area:1", "area:2", "area:3" };
        var registry = CreateGraphRegistry(nodeIds, GraphNodeType.Area, maxInlineRecords: 10, out var exportService, out var tempRoot);
        try
        {
            var result = await registry.GetNodesAsync("tenant-b", GraphNodeType.Area, limit: 2, CancellationToken.None);
            result.Mode.Should().Be("inline");
            result.Items.Should().HaveCount(2);
            result.Count.Should().Be(2);
            result.TotalCount.Should().Be(nodeIds.Length);
            result.Url.Should().BeNull();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static GraphRegistryService CreateGraphRegistry(
        IReadOnlyList<string> nodeIds,
        GraphNodeType nodeType,
        int maxInlineRecords,
        out RegistryExportService exportService,
        out string tempRoot)
    {
        tempRoot = CreateTempDirectory();
        var options = Options.Create(new RegistryExportOptions
        {
            ExportRoot = tempRoot,
            MaxInlineRecords = maxInlineRecords,
            DefaultTtlMinutes = 5
        });

        exportService = new RegistryExportService(options, NullLogger<RegistryExportService>.Instance);
        var clusterMock = BuildClusterMock(nodeType, nodeIds);
        return new GraphRegistryService(clusterMock.Object, exportService, options, NullLogger<GraphRegistryService>.Instance);
    }

    private static Mock<IClusterClient> BuildClusterMock(GraphNodeType nodeType, IReadOnlyList<string> nodeIds)
    {
        var indexMock = new Mock<IGraphIndexGrain>();
        indexMock.Setup(x => x.GetByTypeAsync(nodeType)).ReturnsAsync(nodeIds);

        var snapshots = nodeIds.ToDictionary(
            id => id,
            id => new GraphNodeSnapshot
            {
                Node = new GraphNodeDefinition
                {
                    NodeId = id,
                    NodeType = nodeType,
                    DisplayName = $"{id}-display",
                    Attributes = new Dictionary<string, string> { ["identifier"] = id }
                }
            });

        var nodeMocks = snapshots.ToDictionary(
            kvp => kvp.Key,
            kvp =>
            {
                var grainMock = new Mock<IGraphNodeGrain>();
                grainMock.Setup(g => g.GetAsync()).ReturnsAsync(kvp.Value);
                return grainMock.Object;
            });

        var clusterMock = new Mock<IClusterClient>();
        clusterMock
            .Setup(c => c.GetGrain<IGraphIndexGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(indexMock.Object);
        clusterMock
            .Setup(c => c.GetGrain<IGraphNodeGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((key, _) =>
            {
                var nodeId = ExtractNodeId(key);
                return nodeMocks[nodeId];
            });

        return clusterMock;
    }

    private static string ExtractNodeId(string key)
    {
        var colon = key.IndexOf(':');
        return colon >= 0 ? key[(colon + 1)..] : key;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "graph-registry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
