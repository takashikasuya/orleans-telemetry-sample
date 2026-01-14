using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ApiGateway.Services;
using FluentAssertions;
using Grains.Abstractions;
using Xunit;

namespace ApiGateway.Tests;

public sealed class RegistryEndpointsTests
{
    public static readonly TheoryData<GraphNodeType> NodeTypes = new()
    {
        GraphNodeType.Site,
        GraphNodeType.Building,
        GraphNodeType.Level,
        GraphNodeType.Area,
        GraphNodeType.Equipment,
        GraphNodeType.Point
    };

    [Theory]
    [MemberData(nameof(NodeTypes))]
    public async Task GetNodesAsync_ReturnsNodesPerType(GraphNodeType nodeType)
    {
        var nodesByType = new Dictionary<GraphNodeType, IReadOnlyList<string>>
        {
            [nodeType] = new[] { $"{nodeType}:1" }
        };

        var registry = GraphRegistryTestHelper.CreateGraphRegistry(nodesByType, maxInlineRecords: 10, out _, out var tempRoot);
        try
        {
            var result = await registry.GetNodesAsync("tenant-test", nodeType, null, CancellationToken.None);
            result.Mode.Should().Be("inline");
            result.Items.Should().HaveCount(1);
            result.Items![0].NodeType.Should().Be(nodeType);
            result.TotalCount.Should().Be(1);
        }
        finally
        {
            GraphRegistryTestHelper.CleanupTempDirectory(tempRoot);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task GetNodesAsync_WithNonPositiveLimit_ReturnsEmpty(int limit)
    {
        var nodesByType = new Dictionary<GraphNodeType, IReadOnlyList<string>>
        {
            [GraphNodeType.Area] = new[] { "area:1", "area:2" }
        };

        var registry = GraphRegistryTestHelper.CreateGraphRegistry(nodesByType, maxInlineRecords: 10, out _, out var tempRoot);
        try
        {
            var result = await registry.GetNodesAsync("tenant-test", GraphNodeType.Area, limit, CancellationToken.None);
            result.Count.Should().Be(0);
            result.TotalCount.Should().Be(2);
            result.Items.Should().BeEmpty();
        }
        finally
        {
            GraphRegistryTestHelper.CleanupTempDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task GetNodesAsync_WithLimitLessThanTotal_ReturnsLimitedItems()
    {
        var nodeIds = new[] { "point:1", "point:2", "point:3" };
        var nodesByType = new Dictionary<GraphNodeType, IReadOnlyList<string>>
        {
            [GraphNodeType.Point] = nodeIds
        };

        var registry = GraphRegistryTestHelper.CreateGraphRegistry(nodesByType, maxInlineRecords: 10, out _, out var tempRoot);
        try
        {
            var result = await registry.GetNodesAsync("tenant-test", GraphNodeType.Point, limit: 2, CancellationToken.None);
            result.Mode.Should().Be("inline");
            result.Items.Should().HaveCount(2);
            result.Count.Should().Be(2);
            result.TotalCount.Should().Be(3);
        }
        finally
        {
            GraphRegistryTestHelper.CleanupTempDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task GetNodesAsync_WhenCountExceedsInlineLimit_ReturnsUrl()
    {
        var nodeIds = new[] { "device:1", "device:2", "device:3" };
        var nodesByType = new Dictionary<GraphNodeType, IReadOnlyList<string>>
        {
            [GraphNodeType.Equipment] = nodeIds
        };

        var registry = GraphRegistryTestHelper.CreateGraphRegistry(nodesByType, maxInlineRecords: 2, out var exportService, out var tempRoot);
        try
        {
            var result = await registry.GetNodesAsync("tenant-test", GraphNodeType.Equipment, null, CancellationToken.None);
            result.Mode.Should().Be("url");
            result.TotalCount.Should().Be(3);
            result.Url.Should().NotBeNullOrWhiteSpace();
            result.ExpiresAt.Should().HaveValue();
            result.Count.Should().Be(result.TotalCount);

            var exportId = result.Url!.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
            var opened = await exportService.TryOpenExportAsync(exportId, "tenant-test", DateTimeOffset.UtcNow, CancellationToken.None);
            opened.Status.Should().Be(RegistryExportOpenStatus.Ready);
            opened.Stream.Should().NotBeNull();
            await opened.Stream!.DisposeAsync();
        }
        finally
        {
            GraphRegistryTestHelper.CleanupTempDirectory(tempRoot);
        }
    }
}
