using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Grains.Abstractions;
using Moq;
using Xunit;

namespace ApiGateway.Tests;

public sealed class PointPropertiesTests
{
    static PointPropertiesTests()
    {
        Environment.SetEnvironmentVariable("Orleans__DisableClient", "true");
    }

    [Fact]
    public async Task GetNode_IncludesPointsWithValueAndUpdatedAt()
    {
        var tenant = "tenant-a";
        var nodeId = "node-1";
        var pointNodeId = "point-1";
        var updatedAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var nodeSnapshot = new GraphNodeSnapshot
        {
            Node = new GraphNodeDefinition
            {
                NodeId = nodeId,
                NodeType = GraphNodeType.Equipment
            },
            OutgoingEdges = new List<GraphEdge>
            {
                new GraphEdge { Predicate = "hasPoint", TargetNodeId = pointNodeId }
            }
        };

        var pointSnapshot = new GraphNodeSnapshot
        {
            Node = new GraphNodeDefinition
            {
                NodeId = pointNodeId,
                NodeType = GraphNodeType.Point,
                Attributes = new Dictionary<string, string>
                {
                    ["PointId"] = "p1",
                    ["DeviceId"] = "device-1",
                    ["PointType"] = "temperature",
                    ["BuildingName"] = "b1",
                    ["SpaceId"] = "s1"
                }
            }
        };

        var pointGrainMock = new Mock<IPointGrain>();
        pointGrainMock
            .Setup(g => g.GetAsync())
            .ReturnsAsync(new PointSnapshot(10, 21.5, updatedAt));

        var clusterMock = BuildClusterMock(
            tenant,
            nodeSnapshot,
            pointSnapshot,
            pointGrainMock.Object);

        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var client = factory.CreateClient();

        TestAuthHandler.Reset();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", $"tenant={tenant}");

        var response = await client.GetAsync($"/api/nodes/{nodeId}");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("points", out var points).Should().BeTrue();
        points.TryGetProperty("temperature", out var tempPoint).Should().BeTrue();
        tempPoint.TryGetProperty("value", out var value).Should().BeTrue();
        tempPoint.TryGetProperty("updatedAt", out var updatedAtProp).Should().BeTrue();
        value.GetDouble().Should().Be(21.5);
        updatedAtProp.GetDateTimeOffset().Should().Be(updatedAt);
        tempPoint.EnumerateObject().Should().HaveCount(2);
    }

    [Fact]
    public async Task GetDevice_IncludesPointsWithValueAndUpdatedAt()
    {
        var tenant = "tenant-a";
        var deviceId = "device-1";
        var equipmentId = "equip-1";
        var pointNodeId = "point-1";
        var updatedAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var equipmentSnapshot = new GraphNodeSnapshot
        {
            Node = new GraphNodeDefinition
            {
                NodeId = equipmentId,
                NodeType = GraphNodeType.Equipment,
                Attributes = new Dictionary<string, string>
                {
                    ["DeviceId"] = deviceId
                }
            },
            OutgoingEdges = new List<GraphEdge>
            {
                new GraphEdge { Predicate = "hasPoint", TargetNodeId = pointNodeId }
            }
        };

        var pointSnapshot = new GraphNodeSnapshot
        {
            Node = new GraphNodeDefinition
            {
                NodeId = pointNodeId,
                NodeType = GraphNodeType.Point,
                Attributes = new Dictionary<string, string>
                {
                    ["PointId"] = "p1",
                    ["DeviceId"] = deviceId,
                    ["PointType"] = "temperature",
                    ["BuildingName"] = "b1",
                    ["SpaceId"] = "s1"
                }
            }
        };

        var deviceGrainMock = new Mock<IDeviceGrain>();
        deviceGrainMock
            .Setup(g => g.GetAsync())
            .ReturnsAsync(new DeviceSnapshot(5, new Dictionary<string, object>(), updatedAt));

        var pointGrainMock = new Mock<IPointGrain>();
        pointGrainMock
            .Setup(g => g.GetAsync())
            .ReturnsAsync(new PointSnapshot(10, 22.0, updatedAt));

        var clusterMock = BuildClusterMock(
            tenant,
            equipmentSnapshot,
            pointSnapshot,
            pointGrainMock.Object,
            deviceGrainMock.Object,
            equipmentId,
            deviceId);

        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var client = factory.CreateClient();

        TestAuthHandler.Reset();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", $"tenant={tenant}");

        var response = await client.GetAsync($"/api/devices/{deviceId}");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("points", out var points).Should().BeTrue();
        points.TryGetProperty("temperature", out var tempPoint).Should().BeTrue();
        tempPoint.TryGetProperty("value", out var value).Should().BeTrue();
        tempPoint.TryGetProperty("updatedAt", out var updatedAtProp).Should().BeTrue();
        value.GetDouble().Should().Be(22.0);
        updatedAtProp.GetDateTimeOffset().Should().Be(updatedAt);
        tempPoint.EnumerateObject().Should().HaveCount(2);
    }

    private static Mock<IClusterClient> BuildClusterMock(
        string tenant,
        GraphNodeSnapshot primaryNode,
        GraphNodeSnapshot pointNode,
        IPointGrain pointGrain,
        IDeviceGrain? deviceGrain = null,
        string? equipmentId = null,
        string? deviceId = null)
    {
        var nodeGrains = new Dictionary<string, IGraphNodeGrain>(StringComparer.OrdinalIgnoreCase)
        {
            [primaryNode.Node.NodeId] = BuildNodeGrain(primaryNode),
            [pointNode.Node.NodeId] = BuildNodeGrain(pointNode)
        };

        var indexMock = new Mock<IGraphIndexGrain>();
        if (!string.IsNullOrWhiteSpace(equipmentId))
        {
            indexMock
                .Setup(g => g.GetByTypeAsync(GraphNodeType.Equipment))
                .ReturnsAsync(new[] { equipmentId });
        }

        var clusterMock = new Mock<IClusterClient>();
        clusterMock
            .Setup(c => c.GetGrain<IGraphNodeGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((key, _) =>
            {
                var nodeId = ExtractNodeId(key);
                return nodeGrains.TryGetValue(nodeId, out var grain)
                    ? grain
                    : throw new KeyNotFoundException($"No grain registered for node '{nodeId}'.");
            });
        clusterMock
            .Setup(c => c.GetGrain<IPointGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(pointGrain);
        clusterMock
            .Setup(c => c.GetGrain<IGraphIndexGrain>(tenant, It.IsAny<string?>()))
            .Returns(indexMock.Object);

        if (deviceGrain is not null)
        {
            var deviceKey = DeviceGrainKey.Create(tenant, deviceId ?? "device-1");
            clusterMock
                .Setup(c => c.GetGrain<IDeviceGrain>(deviceKey, It.IsAny<string?>()))
                .Returns(deviceGrain);
        }

        return clusterMock;
    }

    private static IGraphNodeGrain BuildNodeGrain(GraphNodeSnapshot snapshot)
    {
        var grainMock = new Mock<IGraphNodeGrain>();
        grainMock.Setup(g => g.GetAsync()).ReturnsAsync(snapshot);
        return grainMock.Object;
    }

    private static string ExtractNodeId(string key)
    {
        var colonIndex = key.IndexOf(':');
        return colonIndex >= 0 ? key[(colonIndex + 1)..] : key;
    }
}
