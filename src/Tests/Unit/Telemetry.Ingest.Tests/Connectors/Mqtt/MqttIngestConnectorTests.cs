using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Grains.Abstractions;
using Telemetry.Ingest.Mqtt;
using Xunit;

namespace Telemetry.Ingest.Tests.Connectors.Mqtt;

public sealed class MqttIngestConnectorTests
{
    [Fact]
    public void TryParseTelemetryPoint_ParsesTopicRegexAndPayload()
    {
        var bindings = MqttIngestConnector.CompileBindings(new[]
        {
            new MqttTopicBindingOptions
            {
                Filter = "tenants/+/devices/+/points/+",
                TopicRegex = "^tenants/(?<tenantId>[^/]+)/devices/(?<deviceId>[^/]+)/points/(?<pointId>[^/]+)$"
            }
        });

        var payload = Encoding.UTF8.GetBytes("""
        {
          "value": 22.5,
          "datetime": "2026-02-14T09:30:00Z"
        }
        """);

        var ok = MqttIngestConnector.TryParseTelemetryPoint(
            "tenants/t1/devices/dev-1/points/p1",
            payload,
            bindings,
            new MqttPayloadOptions(),
            sequence: 7,
            receivedAt: DateTimeOffset.UtcNow,
            out var message,
            out var reason);

        ok.Should().BeTrue();
        reason.Should().BeEmpty();
        message.Should().NotBeNull();
        message!.TenantId.Should().Be("t1");
        message.DeviceId.Should().Be("dev-1");
        message.PointId.Should().Be("p1");
        message.Sequence.Should().Be(7);
        message.Value.Should().Be(22.5);
        message.Timestamp.Should().Be(DateTimeOffset.Parse("2026-02-14T09:30:00Z"));
    }

    [Fact]
    public void TryParseTelemetryPoint_ReturnsFalse_WhenTopicRegexMismatch()
    {
        var bindings = MqttIngestConnector.CompileBindings(new[]
        {
            new MqttTopicBindingOptions
            {
                Filter = "tenants/+/devices/+/points/+",
                TopicRegex = "^tenants/(?<tenantId>[^/]+)/devices/(?<deviceId>[^/]+)/points/(?<pointId>[^/]+)$"
            }
        });

        var payload = Encoding.UTF8.GetBytes("""{"value": 1, "datetime": "2026-02-14T09:30:00Z"}""");

        var ok = MqttIngestConnector.TryParseTelemetryPoint(
            "x/t1/d/dev-1/p/p1",
            payload,
            bindings,
            new MqttPayloadOptions(),
            sequence: 1,
            receivedAt: DateTimeOffset.UtcNow,
            out _,
            out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("topic_regex_no_match");
    }

    [Fact]
    public void TryParseTelemetryPoint_FallsBackToReceivedTime_WhenDatetimeInvalid()
    {
        var bindings = MqttIngestConnector.CompileBindings(new[]
        {
            new MqttTopicBindingOptions
            {
                Filter = "tenants/+/devices/+/points/+",
                TopicRegex = "^tenants/(?<tenantId>[^/]+)/devices/(?<deviceId>[^/]+)/points/(?<pointId>[^/]+)$"
            }
        });

        var payload = Encoding.UTF8.GetBytes("""{"value": true, "datetime": "invalid"}""");
        var receivedAt = new DateTimeOffset(2026, 2, 14, 9, 45, 0, TimeSpan.Zero);

        var ok = MqttIngestConnector.TryParseTelemetryPoint(
            "tenants/t1/devices/dev-1/points/p1",
            payload,
            bindings,
            new MqttPayloadOptions(),
            sequence: 2,
            receivedAt,
            out var message,
            out _);

        ok.Should().BeTrue();
        message.Should().NotBeNull();
        message!.Timestamp.Should().Be(receivedAt);
        message.Value.Should().Be(true);
    }

    [Fact]
    public async Task WriteWithPolicyAsync_DropNewest_ReturnsFalse_WhenChannelFull()
    {
        var channel = Channel.CreateBounded<TelemetryPointMsg>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        channel.Writer.TryWrite(new TelemetryPointMsg { TenantId = "t1", DeviceId = "d1", PointId = "p1" }).Should().BeTrue();

        var result = await MqttIngestConnector.WriteWithPolicyAsync(
            channel.Writer,
            new TelemetryPointMsg { TenantId = "t1", DeviceId = "d1", PointId = "p2" },
            MqttDropPolicy.DropNewest,
            timeoutMs: 10,
            CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task WriteWithPolicyAsync_FailFast_Throws_WhenChannelFull()
    {
        var channel = Channel.CreateBounded<TelemetryPointMsg>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        channel.Writer.TryWrite(new TelemetryPointMsg { TenantId = "t1", DeviceId = "d1", PointId = "p1" }).Should().BeTrue();

        Func<Task> act = async () => await MqttIngestConnector.WriteWithPolicyAsync(
            channel.Writer,
            new TelemetryPointMsg { TenantId = "t1", DeviceId = "d1", PointId = "p2" },
            MqttDropPolicy.FailFast,
            timeoutMs: 10,
            CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }
}
