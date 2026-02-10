using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ApiGateway.Services;
using FluentAssertions;
using Grains.Abstractions;
using Moq;
using Orleans;
using Xunit;

namespace ApiGateway.Tests;

public sealed class TagSearchServiceTests
{
    [Fact]
    public async Task SearchNodesByTagsAsync_ReturnsOnlyNodesMatchingAllTags()
    {
        var service = CreateService();

        var response = await service.SearchNodesByTagsAsync("tenant-1", new[] { "hot", "zone-a" }, null, CancellationToken.None);

        response.Count.Should().Be(1);
        response.Items[0].NodeId.Should().Be("point:temp-1");
        response.Items[0].MatchedTags.Should().BeEquivalentTo(new[] { "hot", "zone-a" });
    }

    [Fact]
    public async Task SearchGrainsByTagsAsync_ReturnsDeviceAndPointGrains()
    {
        var service = CreateService();

        var response = await service.SearchGrainsByTagsAsync("tenant-1", new[] { "hot" }, null, CancellationToken.None);

        response.Count.Should().Be(2);
        response.Items.Should().ContainSingle(x => x.GrainType == "Device" && x.GrainKey == "tenant-1:device-01");
        response.Items.Should().ContainSingle(x => x.GrainType == "Point" && x.GrainKey == "tenant-1:BuildingA:Room101:device-01:temp");
    }

    [Fact]
    public async Task SearchNodesByTagsAsync_WithNonPositiveLimit_ReturnsEmpty()
    {
        var service = CreateService();

        var response = await service.SearchNodesByTagsAsync("tenant-1", new[] { "hot" }, 0, CancellationToken.None);

        response.Count.Should().Be(0);
    }

    private static TagSearchService CreateService()
    {
        var tagIndexMock = new Mock<IGraphTagIndexGrain>();
        tagIndexMock
            .Setup(x => x.GetNodeIdsByTagsAsync(It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync((IReadOnlyList<string> tags) =>
            {
                var normalized = tags.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
                if (normalized.SetEquals(new[] { "hot", "zone-a" }))
                {
                    return new[] { "point:temp-1" };
                }

                if (normalized.SetEquals(new[] { "hot" }))
                {
                    return new[] { "equipment:boiler-1", "point:temp-1" };
                }

                return System.Array.Empty<string>();
            });

        var nodeSnapshots = new Dictionary<string, GraphNodeSnapshot>
        {
            ["equipment:boiler-1"] = new()
            {
                Node = new GraphNodeDefinition
                {
                    NodeId = "equipment:boiler-1",
                    NodeType = GraphNodeType.Equipment,
                    DisplayName = "Boiler 1",
                    Attributes = new Dictionary<string, string>
                    {
                        ["DeviceId"] = "device-01",
                        ["tag:hot"] = "true"
                    }
                }
            },
            ["point:temp-1"] = new()
            {
                Node = new GraphNodeDefinition
                {
                    NodeId = "point:temp-1",
                    NodeType = GraphNodeType.Point,
                    DisplayName = "Temperature",
                    Attributes = new Dictionary<string, string>
                    {
                        ["DeviceId"] = "device-01",
                        ["PointId"] = "temp",
                        ["BuildingName"] = "BuildingA",
                        ["SpaceId"] = "Room101",
                        ["tag:hot"] = "TRUE",
                        ["tag:zone-a"] = "1"
                    }
                }
            }
        };

        var clientMock = new Mock<IClusterClient>();
        clientMock
            .Setup(c => c.GetGrain<IGraphTagIndexGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(tagIndexMock.Object);

        clientMock
            .Setup(c => c.GetGrain<IGraphNodeGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((key, _) =>
            {
                var nodeId = key[(key.IndexOf(':') + 1)..];
                var grain = new Mock<IGraphNodeGrain>();
                grain.Setup(x => x.GetAsync()).ReturnsAsync(nodeSnapshots[nodeId]);
                return grain.Object;
            });

        return new TagSearchService(clientMock.Object);
    }
}
