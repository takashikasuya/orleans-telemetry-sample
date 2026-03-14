using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Devices.V1;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Grains.Abstractions;
using Moq;
using Orleans;
using Xunit;

namespace ApiGateway.Tests;

public sealed class GrpcRegistryServiceTests
{
    [Fact]
    public async Task SearchByTags_ReturnsMatchingNodes()
    {
        var clusterMock = BuildClusterMock();
        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var channel = CreateChannel(factory);
        var client = new global::Devices.V1.RegistryService.RegistryServiceClient(channel);

        var call = client.SearchByTagsAsync(new TagSearchRequest { Tags = { "hot" } }, BuildAuthHeaders());
        var response = await call.ResponseAsync;

        response.Count.Should().Be(2);
        response.Items.Should().Contain(x => x.NodeId == "equipment:boiler-1");
        response.Items.Should().Contain(x => x.NodeId == "point:temp-1");
    }

    [Fact]
    public async Task SearchGrainsByTags_ReturnsDerivedGrains()
    {
        var clusterMock = BuildClusterMock();
        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var channel = CreateChannel(factory);
        var client = new global::Devices.V1.RegistryService.RegistryServiceClient(channel);

        var call = client.SearchGrainsByTagsAsync(new TagSearchRequest { Tags = { "hot", "zone-a" } }, BuildAuthHeaders());
        var response = await call.ResponseAsync;

        response.Count.Should().Be(1);
        response.Items[0].GrainType.Should().Be("Point");
        response.Items[0].GrainKey.Should().Be("t1:temp");
    }

    [Fact]
    public async Task SearchByTags_WithoutTags_ReturnsInvalidArgument()
    {
        var clusterMock = BuildClusterMock();
        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var channel = CreateChannel(factory);
        var client = new global::Devices.V1.RegistryService.RegistryServiceClient(channel);

        var act = async () =>
        {
            var call = client.SearchByTagsAsync(new TagSearchRequest(), BuildAuthHeaders());
            _ = await call.ResponseAsync;
        };

        var ex = await Assert.ThrowsAsync<RpcException>(act);
        ex.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    private static Mock<IClusterClient> BuildClusterMock()
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

        var snapshots = new Dictionary<string, GraphNodeSnapshot>
        {
            ["equipment:boiler-1"] = new()
            {
                Node = new GraphNodeDefinition
                {
                    NodeId = "equipment:boiler-1",
                    NodeType = GraphNodeType.Equipment,
                    DisplayName = "Boiler",
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
                    DisplayName = "Temp",
                    Attributes = new Dictionary<string, string>
                    {
                        ["DeviceId"] = "device-01",
                        ["PointId"] = "temp",
                        ["BuildingName"] = "BuildingA",
                        ["SpaceId"] = "Room101",
                        ["tag:hot"] = "true",
                        ["tag:zone-a"] = "true"
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
                grain.Setup(x => x.GetAsync()).ReturnsAsync(snapshots[nodeId]);
                return grain.Object;
            });

        return clientMock;
    }

    private static Metadata BuildAuthHeaders() => new()
    {
        { "authorization", "Test tenant=t1" }
    };

    private static GrpcChannel CreateChannel(ApiGatewayTestFactory factory) =>
        GrpcChannel.ForAddress(factory.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = factory.Server.CreateHandler()
        });
}
