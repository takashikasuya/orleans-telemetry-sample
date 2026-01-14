using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ApiGateway.Services;
using FluentAssertions;
using Grains.Abstractions;
using Xunit;

namespace ApiGateway.Tests;

public sealed class GraphRegistryServiceTests
{
    [Fact]
    public async Task GetNodesAsync_WhenNodeCountExceedsInlineLimit_CreatesExport()
    {
        var nodeIds = new[] { "building:1", "building:2", "building:3" };
        var nodesByType = new Dictionary<GraphNodeType, IReadOnlyList<string>>
        {
            [GraphNodeType.Building] = nodeIds
        };

        var registry = GraphRegistryTestHelper.CreateGraphRegistry(nodesByType, maxInlineRecords: 2, out var exportService, out var tempRoot);
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
            GraphRegistryTestHelper.CleanupTempDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task GetNodesAsync_WithLimit_ReturnsInlineSubset()
    {
        var nodeIds = new[] { "area:1", "area:2", "area:3" };
        var nodesByType = new Dictionary<GraphNodeType, IReadOnlyList<string>>
        {
            [GraphNodeType.Area] = nodeIds
        };

        var registry = GraphRegistryTestHelper.CreateGraphRegistry(nodesByType, maxInlineRecords: 10, out var exportService, out var tempRoot);
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
            GraphRegistryTestHelper.CleanupTempDirectory(tempRoot);
        }
    }
}
