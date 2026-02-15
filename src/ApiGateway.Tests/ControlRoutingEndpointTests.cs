using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ApiGateway.Contracts;
using ApiPointControlRequest = ApiGateway.Contracts.PointControlRequest;
using FluentAssertions;
using Grains.Abstractions;
using Moq;
using Xunit;

namespace ApiGateway.Tests;

public sealed class ControlRoutingEndpointTests
{
    [Fact]
    public async Task PostControl_RoutesConnectorByGatewayRegex()
    {
        var tenant = "tenant-a";
        var deviceId = "ahu-01";
        var pointId = "setpoint-temp";
        var equipmentNodeId = "equip-1";
        var pointNodeId = "point-1";

        var equipmentSnapshot = new GraphNodeSnapshot
        {
            Node = new GraphNodeDefinition
            {
                NodeId = equipmentNodeId,
                NodeType = GraphNodeType.Equipment,
                Attributes = new Dictionary<string, string>
                {
                    ["DeviceId"] = deviceId,
                    ["GatewayId"] = "mqtt-east-01"
                }
            },
            OutgoingEdges = new List<GraphEdge>
            {
                new() { Predicate = "hasPoint", TargetNodeId = pointNodeId }
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
                    ["PointId"] = pointId,
                    ["DeviceId"] = deviceId
                }
            }
        };

        var clusterMock = BuildClusterMock(
            tenant,
            equipmentNodeId,
            equipmentSnapshot,
            pointSnapshot,
            expectedConnector: "Mqtt");

        var extraConfig = new Dictionary<string, string?>
        {
            ["ControlRouting:DefaultConnector"] = "RabbitMq",
            ["ControlRouting:Rules:0:Name"] = "mqtt-rule",
            ["ControlRouting:Rules:0:Connector"] = "Mqtt",
            ["ControlRouting:Rules:0:GatewayPattern"] = "^mqtt-"
        };

        await using var factory = new ApiGatewayTestFactory(clusterMock, extraConfig);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", $"tenant={tenant}");

        var request = new ApiPointControlRequest(
            CommandId: string.Empty,
            BuildingName: "b1",
            SpaceId: "s1",
            DeviceId: deviceId,
            PointId: pointId,
            DesiredValue: 22.5,
            Metadata: new Dictionary<string, string>());

        var response = await client.PostAsJsonAsync($"/api/devices/{deviceId}/control", request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var payload = await response.Content.ReadFromJsonAsync<PointControlResponse>();
        payload.Should().NotBeNull();
        payload!.ConnectorName.Should().Be("Mqtt");
    }

    [Fact]
    public async Task PostControl_ReturnsBadRequest_WhenNoConnectorRouteMatched()
    {
        var tenant = "tenant-a";
        var deviceId = "ahu-01";
        var pointId = "point-a";
        var equipmentNodeId = "equip-1";
        var pointNodeId = "point-1";

        var equipmentSnapshot = new GraphNodeSnapshot
        {
            Node = new GraphNodeDefinition
            {
                NodeId = equipmentNodeId,
                NodeType = GraphNodeType.Equipment,
                Attributes = new Dictionary<string, string>
                {
                    ["DeviceId"] = deviceId,
                    ["GatewayId"] = "unknown-gw"
                }
            },
            OutgoingEdges = new List<GraphEdge>
            {
                new() { Predicate = "hasPoint", TargetNodeId = pointNodeId }
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
                    ["PointId"] = pointId,
                    ["DeviceId"] = deviceId
                }
            }
        };

        var clusterMock = BuildClusterMock(
            tenant,
            equipmentNodeId,
            equipmentSnapshot,
            pointSnapshot,
            expectedConnector: null);

        var extraConfig = new Dictionary<string, string?>
        {
            ["ControlRouting:DefaultConnector"] = "",
            ["ControlRouting:Rules:0:Name"] = "mqtt-rule",
            ["ControlRouting:Rules:0:Connector"] = "Mqtt",
            ["ControlRouting:Rules:0:GatewayPattern"] = "^mqtt-"
        };

        await using var factory = new ApiGatewayTestFactory(clusterMock, extraConfig);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", $"tenant={tenant}");

        var request = new ApiPointControlRequest(
            CommandId: string.Empty,
            BuildingName: "b1",
            SpaceId: "s1",
            DeviceId: deviceId,
            PointId: pointId,
            DesiredValue: 22.5,
            Metadata: new Dictionary<string, string>());

        var response = await client.PostAsJsonAsync($"/api/devices/{deviceId}/control", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static Mock<IClusterClient> BuildClusterMock(
        string tenant,
        string equipmentNodeId,
        GraphNodeSnapshot equipmentSnapshot,
        GraphNodeSnapshot pointSnapshot,
        string? expectedConnector)
    {
        var graphIndex = new Mock<IGraphIndexGrain>();
        graphIndex
            .Setup(g => g.GetByTypeAsync(GraphNodeType.Equipment))
            .ReturnsAsync(new[] { equipmentNodeId });

        var equipmentNode = new Mock<IGraphNodeGrain>();
        equipmentNode.Setup(g => g.GetAsync()).ReturnsAsync(equipmentSnapshot);

        var pointNode = new Mock<IGraphNodeGrain>();
        pointNode.Setup(g => g.GetAsync()).ReturnsAsync(pointSnapshot);

        var controlGrain = new Mock<IPointControlGrain>();
        controlGrain
            .Setup(g => g.SubmitAsync(It.IsAny<Grains.Abstractions.PointControlRequest>()))
            .ReturnsAsync((Grains.Abstractions.PointControlRequest req) =>
            {
                if (expectedConnector is not null)
                {
                    req.Metadata.Should().ContainKey("ConnectorName");
                    req.Metadata["ConnectorName"].Should().Be(expectedConnector);
                }

                req.Metadata.TryGetValue("ConnectorName", out var connectorName);
                req.Metadata.TryGetValue("CorrelationId", out var correlationId);
                req.Metadata.TryGetValue("LastError", out var lastError);

                return new PointControlSnapshot(
                    req.CommandId,
                    ControlRequestStatus.Accepted,
                    req.DesiredValue,
                    req.RequestedAt,
                    DateTimeOffset.UtcNow,
                    null,
                    connectorName,
                    correlationId,
                    lastError);
            });

        var clusterMock = new Mock<IClusterClient>();
        clusterMock
            .Setup(c => c.GetGrain<IGraphIndexGrain>(tenant, It.IsAny<string?>()))
            .Returns(graphIndex.Object);
        clusterMock
            .Setup(c => c.GetGrain<IGraphNodeGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((key, _) =>
            {
                var nodeId = ExtractNodeId(key);
                if (string.Equals(nodeId, equipmentNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    return equipmentNode.Object;
                }

                if (string.Equals(nodeId, pointSnapshot.Node.NodeId, StringComparison.OrdinalIgnoreCase))
                {
                    return pointNode.Object;
                }

                throw new KeyNotFoundException($"Unmapped node id: {nodeId}");
            });
        clusterMock
            .Setup(c => c.GetGrain<IPointControlGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(controlGrain.Object);

        return clusterMock;
    }

    private static string ExtractNodeId(string key)
    {
        var colon = key.IndexOf(':');
        return colon >= 0 ? key[(colon + 1)..] : key;
    }
}
