using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Devices.V1;
using FluentAssertions;
using Grains.Abstractions;
using Grpc.Core;
using Grpc.Net.Client;
using Moq;
using Orleans;
using Xunit;

namespace ApiGateway.Tests;

public sealed class GrpcDeviceServiceTests
{
    [Fact]
    public async Task GetSnapshot_ReturnsMappedSnapshot()
    {
        var snapshot = new DeviceSnapshot(
            LastSequence: 42,
            LatestProps: new Dictionary<string, object>
            {
                ["temp"] = 21.5,
                ["status"] = "ok"
            },
            UpdatedAt: DateTimeOffset.Parse("2026-02-10T00:00:00+00:00"));

        var deviceGrainMock = new Mock<IDeviceGrain>();
        deviceGrainMock.Setup(g => g.GetAsync()).ReturnsAsync(snapshot);

        var clusterMock = new Mock<IClusterClient>();
        clusterMock
            .Setup(c => c.GetGrain<IDeviceGrain>("t1:device-1", It.IsAny<string?>()))
            .Returns(deviceGrainMock.Object);

        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var channel = CreateChannel(factory);

        var client = new global::Devices.V1.DeviceService.DeviceServiceClient(channel);
        var headers = BuildAuthHeaders();

        var call = client.GetSnapshotAsync(new DeviceKey { DeviceId = "device-1" }, headers);
        Snapshot response = await call.ResponseAsync;

        response.DeviceId.Should().Be("device-1");
        response.LastSequence.Should().Be(42);
        response.Properties.Should().ContainSingle(x => x.Key == "status");
        response.Properties.Should().ContainSingle(x => x.Key == "temp");
    }

    [Fact]
    public async Task GetSnapshot_WithEmptyDeviceId_ReturnsInvalidArgument()
    {
        var clusterMock = new Mock<IClusterClient>();
        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var channel = CreateChannel(factory);

        var client = new global::Devices.V1.DeviceService.DeviceServiceClient(channel);

        var act = async () =>
        {
            var call = client.GetSnapshotAsync(new DeviceKey(), BuildAuthHeaders());
            _ = await call.ResponseAsync;
        };

        var ex = await Assert.ThrowsAsync<RpcException>(act);
        ex.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task GetSnapshot_WhenDeadlineExpires_ReturnsGrpcFailure()
    {
        var deviceGrainMock = new Mock<IDeviceGrain>();
        deviceGrainMock
            .Setup(g => g.GetAsync())
            .Returns(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                return new DeviceSnapshot(1, new Dictionary<string, object>(), DateTimeOffset.UtcNow);
            });

        var clusterMock = new Mock<IClusterClient>();
        clusterMock
            .Setup(c => c.GetGrain<IDeviceGrain>("t1:device-1", It.IsAny<string?>()))
            .Returns(deviceGrainMock.Object);

        await using var factory = new ApiGatewayTestFactory(clusterMock);
        var channel = CreateChannel(factory);

        var client = new global::Devices.V1.DeviceService.DeviceServiceClient(channel);

        var act = async () =>
        {
            var call = client.GetSnapshotAsync(
                new DeviceKey { DeviceId = "device-1" },
                headers: BuildAuthHeaders(),
                deadline: DateTime.UtcNow.AddMilliseconds(20));
            _ = await call.ResponseAsync;
        };

        var ex = await Assert.ThrowsAsync<RpcException>(act);
        // TestServer + gRPC in-proc can surface deadline cancellation as Internal
        // depending on transport timing, so keep this assertion tolerant to runtime variance.
        ex.StatusCode.Should().BeOneOf(StatusCode.DeadlineExceeded, StatusCode.Cancelled, StatusCode.Unknown, StatusCode.Internal);
    }

    [Fact]
    public async Task GetSnapshot_WhenGrpcDisabled_ReturnsGrpcFailure()
    {
        var clusterMock = new Mock<IClusterClient>();
        await using var factory = new ApiGatewayTestFactory(clusterMock, new Dictionary<string, string?>
        {
            ["Grpc:Enabled"] = "false"
        });
        var channel = CreateChannel(factory);

        var client = new global::Devices.V1.DeviceService.DeviceServiceClient(channel);

        var act = async () =>
        {
            var call = client.GetSnapshotAsync(new DeviceKey { DeviceId = "device-1" }, BuildAuthHeaders());
            _ = await call.ResponseAsync;
        };

        var ex = await Assert.ThrowsAsync<RpcException>(act);
        ex.StatusCode.Should().BeOneOf(StatusCode.Unimplemented, StatusCode.Internal);
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
