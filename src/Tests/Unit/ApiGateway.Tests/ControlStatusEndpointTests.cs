using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ApiGateway.Contracts;
using FluentAssertions;
using Grains.Abstractions;
using Moq;
using Xunit;

namespace ApiGateway.Tests;

public sealed class ControlStatusEndpointTests
{
    [Fact]
    public async Task GetControl_ReturnsSnapshot_WhenCommandExists()
    {
        var tenant = "tenant-a";
        var deviceId = "ahu-01";
        var commandId = "cmd-status-01";

        var snapshot = new PointControlSnapshot(
            commandId,
            ControlRequestStatus.Accepted,
            22.5,
            DateTimeOffset.UtcNow.AddSeconds(-5),
            DateTimeOffset.UtcNow.AddSeconds(-4),
            null,
            "RabbitMq",
            "corr-42",
            null);

        var clusterMock = BuildClusterMock(tenant, deviceId, commandId, snapshot);

        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", $"tenant={tenant}");

        var response = await client.GetAsync($"/api/devices/{deviceId}/control/{commandId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<PointControlResponse>();
        payload.Should().NotBeNull();
        payload!.CommandId.Should().Be(commandId);
        payload.Status.Should().Be(ControlRequestStatus.Accepted.ToString());
        payload.CorrelationId.Should().Be("corr-42");
        payload.ConnectorName.Should().Be("RabbitMq");
    }

    [Fact]
    public async Task GetControl_ReturnsNotFound_WhenCommandDoesNotExist()
    {
        var tenant = "tenant-a";
        var deviceId = "ahu-01";
        var commandId = "nonexistent-cmd";

        var clusterMock = BuildClusterMock(tenant, deviceId, commandId, snapshot: null);

        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", $"tenant={tenant}");

        var response = await client.GetAsync($"/api/devices/{deviceId}/control/{commandId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetControl_ReturnsFailedStatus_WhenCommandFailed()
    {
        var tenant = "tenant-a";
        var deviceId = "ahu-01";
        var commandId = "cmd-failed-01";

        var snapshot = new PointControlSnapshot(
            commandId,
            ControlRequestStatus.Failed,
            22.5,
            DateTimeOffset.UtcNow.AddSeconds(-10),
            DateTimeOffset.UtcNow.AddSeconds(-9),
            null,
            "RabbitMq",
            null,
            "connection refused");

        var clusterMock = BuildClusterMock(tenant, deviceId, commandId, snapshot);

        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", $"tenant={tenant}");

        var response = await client.GetAsync($"/api/devices/{deviceId}/control/{commandId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<PointControlResponse>();
        payload.Should().NotBeNull();
        payload!.Status.Should().Be(ControlRequestStatus.Failed.ToString());
        payload.LastError.Should().Be("connection refused");
    }

    private static Mock<IClusterClient> BuildClusterMock(
        string tenant,
        string deviceId,
        string commandId,
        PointControlSnapshot? snapshot)
    {
        var indexGrain = new Mock<IDeviceControlIndexGrain>();
        indexGrain
            .Setup(g => g.GetAsync(commandId))
            .ReturnsAsync(snapshot);

        var clusterMock = new Mock<IClusterClient>();
        clusterMock
            .Setup(c => c.GetGrain<IDeviceControlIndexGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(indexGrain.Object);

        return clusterMock;
    }
}
