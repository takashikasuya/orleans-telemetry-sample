using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using ApiGateway.Contracts;
using ApiPointControlRequest = ApiGateway.Contracts.PointControlRequest;
using FluentAssertions;
using Grains.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Telemetry.Ingest;
using Xunit;

namespace ApiGateway.Tests;

public sealed class ControlEgressEndpointTests
{
    [Fact]
    public async Task PostControl_DeliversCommandToEgressConnector()
    {
        var tenant = "tenant-a";
        var deviceId = "ahu-01";
        var pointId = "setpoint-temp";
        var equipmentNodeId = "equip-1";
        var pointNodeId = "point-1";
        const string connectorName = "RabbitMq";

        var equipmentSnapshot = new GraphNodeSnapshot
        {
            Node = new GraphNodeDefinition
            {
                NodeId = equipmentNodeId,
                NodeType = GraphNodeType.Equipment,
                Attributes = new Dictionary<string, string>
                {
                    ["DeviceId"] = deviceId,
                    ["GatewayId"] = "rabbitmq-gw-01"
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

        ControlEgressRequest? capturedRequest = null;
        var egressConnector = new Mock<IControlEgressConnector>();
        egressConnector.Setup(c => c.Name).Returns(connectorName);
        egressConnector.Setup(c => c.ConfirmMode).Returns(ControlConfirmMode.AckOnly);
        egressConnector
            .Setup(c => c.SendAsync(It.IsAny<ControlEgressRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ControlEgressRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new ControlEgressResult
            {
                CommandId = "test-cmd",
                ConnectorName = connectorName,
                Accepted = true,
                ConfirmMode = ControlConfirmMode.AckOnly,
                CorrelationId = "corr-01"
            });

        var clusterMock = BuildClusterMock(
            tenant,
            equipmentNodeId,
            equipmentSnapshot,
            pointSnapshot);

        var extraConfig = new Dictionary<string, string?>
        {
            ["ControlRouting:ConnectorGatewayMappings:0:Connector"] = connectorName,
            ["ControlRouting:ConnectorGatewayMappings:0:GatewayIds:0"] = "rabbitmq-gw-01"
        };

        await using var factory = new ApiGatewayTestFactory(
            clusterMock,
            extraConfig,
            services => services.AddSingleton(_ => egressConnector.Object));
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

        egressConnector.Verify(
            c => c.SendAsync(It.IsAny<ControlEgressRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.DeviceId.Should().Be(deviceId);
        capturedRequest.PointId.Should().Be(pointId);
        capturedRequest.DesiredValue.Should().NotBeNull();
        capturedRequest.Metadata.Should().ContainKey("ConnectorName")
            .WhoseValue.Should().Be(connectorName);
        payload.Should().NotBeNull();
        payload!.CorrelationId.Should().Be("corr-01");
        payload.Status.Should().Be(ControlRequestStatus.Accepted.ToString());
    }

    [Fact]
    public async Task PostControl_UpdatesGrainToFailed_WhenEgressConnectorRejectsCommand()
    {
        var tenant = "tenant-a";
        var deviceId = "ahu-01";
        var pointId = "setpoint-temp";
        var equipmentNodeId = "equip-1";
        var pointNodeId = "point-1";
        const string connectorName = "RabbitMq";

        var equipmentSnapshot = new GraphNodeSnapshot
        {
            Node = new GraphNodeDefinition
            {
                NodeId = equipmentNodeId,
                NodeType = GraphNodeType.Equipment,
                Attributes = new Dictionary<string, string>
                {
                    ["DeviceId"] = deviceId,
                    ["GatewayId"] = "rabbitmq-gw-01"
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

        var egressConnector = new Mock<IControlEgressConnector>();
        egressConnector.Setup(c => c.Name).Returns(connectorName);
        egressConnector.Setup(c => c.ConfirmMode).Returns(ControlConfirmMode.AckOnly);
        egressConnector
            .Setup(c => c.SendAsync(It.IsAny<ControlEgressRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlEgressResult
            {
                CommandId = "test-cmd",
                ConnectorName = connectorName,
                Accepted = false,
                Error = "connection refused",
                ConfirmMode = ControlConfirmMode.AckOnly
            });

        string? updatedCommandId = null;
        ControlRequestStatus? updatedStatus = null;
        string? updatedError = null;
        var clusterMock = BuildClusterMock(
            tenant,
            equipmentNodeId,
            equipmentSnapshot,
            pointSnapshot,
            onUpdate: (commandId, status, _, lastError) =>
            {
                updatedCommandId = commandId;
                updatedStatus = status;
                updatedError = lastError;
            });

        var extraConfig = new Dictionary<string, string?>
        {
            ["ControlRouting:ConnectorGatewayMappings:0:Connector"] = connectorName,
            ["ControlRouting:ConnectorGatewayMappings:0:GatewayIds:0"] = "rabbitmq-gw-01"
        };

        await using var factory = new ApiGatewayTestFactory(
            clusterMock,
            extraConfig,
            services => services.AddSingleton(_ => egressConnector.Object));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", $"tenant={tenant}");

        var request = new ApiPointControlRequest(
            CommandId: "cmd-fail-01",
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
        payload!.Status.Should().Be(ControlRequestStatus.Failed.ToString());
        payload.LastError.Should().Be("connection refused");

        updatedCommandId.Should().Be("cmd-fail-01");
        updatedStatus.Should().Be(ControlRequestStatus.Failed);
        updatedError.Should().Be("connection refused");
    }

    private static Mock<IClusterClient> BuildClusterMock(
        string tenant,
        string equipmentNodeId,
        GraphNodeSnapshot equipmentSnapshot,
        GraphNodeSnapshot pointSnapshot,
        Action<string, ControlRequestStatus, string?, string?>? onUpdate = null)
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
                req.Metadata.TryGetValue("ConnectorName", out var connectorName);
                return new PointControlSnapshot(
                    req.CommandId,
                    ControlRequestStatus.Accepted,
                    req.DesiredValue,
                    req.RequestedAt,
                    DateTimeOffset.UtcNow,
                    null,
                    connectorName,
                    null,
                    null);
            });

        controlGrain
            .Setup(g => g.UpdateAsync(
                It.IsAny<string>(),
                It.IsAny<ControlRequestStatus>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .Callback<string, ControlRequestStatus, string?, string?>(
                (commandId, status, correlationId, lastError) =>
                    onUpdate?.Invoke(commandId, status, correlationId, lastError))
            .Returns(Task.CompletedTask);

        var indexGrain = new Mock<IDeviceControlIndexGrain>();
        indexGrain
            .Setup(g => g.RecordAsync(It.IsAny<string>(), It.IsAny<PointControlSnapshot>()))
            .Returns(Task.CompletedTask);
        indexGrain
            .Setup(g => g.UpdateAsync(
                It.IsAny<string>(),
                It.IsAny<ControlRequestStatus>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

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
        clusterMock
            .Setup(c => c.GetGrain<IDeviceControlIndexGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(indexGrain.Object);

        return clusterMock;
    }

    private static string ExtractNodeId(string key)
    {
        var colon = key.IndexOf(':');
        return colon >= 0 ? key[(colon + 1)..] : key;
    }
}
