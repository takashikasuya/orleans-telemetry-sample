using System.Text;
using System.Text.Json;
using FluentAssertions;
using Grains.Abstractions;
using Telemetry.Ingest.RabbitMq;
using Xunit;

namespace Telemetry.Ingest.Tests.Connectors.RabbitMq;

public sealed class RabbitMqIngestConnectorTests
{
    [Fact]
    public void TryDeserializeTelemetry_ReturnsTrue_ForValidPayload()
    {
        var payload = new TelemetryMsg
        {
            TenantId = "t1",
            BuildingName = "b1",
            SpaceId = "s1",
            DeviceId = "d1",
            Sequence = 10,
            Timestamp = DateTimeOffset.UtcNow,
            Properties = new Dictionary<string, object>
            {
                ["temperature"] = 21.5
            }
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);

        var ok = RabbitMqIngestConnector.TryDeserializeTelemetry(bytes, out var message, out var exception);

        ok.Should().BeTrue();
        exception.Should().BeNull();
        message.Should().NotBeNull();
        message!.DeviceId.Should().Be("d1");
        message.Properties.Should().ContainKey("temperature");
    }

    [Fact]
    public void TryDeserializeTelemetry_ReturnsFalse_ForInvalidPayload()
    {
        var bytes = Encoding.UTF8.GetBytes("{ invalid-json ");

        var ok = RabbitMqIngestConnector.TryDeserializeTelemetry(bytes, out var message, out var exception);

        ok.Should().BeFalse();
        message.Should().BeNull();
        exception.Should().NotBeNull();
    }

    [Fact]
    public void ToTelemetryPointMessages_NormalizesJsonElementValues()
    {
        var metadataElement = JsonDocument.Parse("""
        {
          "source":"sim",
          "tags":["a","b"],
          "enabled":true
        }
        """).RootElement.Clone();

        var message = new TelemetryMsg
        {
            TenantId = "t1",
            BuildingName = "b1",
            SpaceId = "s1",
            DeviceId = "d1",
            Sequence = 42,
            Timestamp = DateTimeOffset.UtcNow,
            Properties = new Dictionary<string, object>
            {
                ["temperature"] = 21.5,
                ["meta"] = metadataElement
            }
        };

        var points = RabbitMqIngestConnector.ToTelemetryPointMessages(message);

        points.Should().HaveCount(2);
        points.Select(p => p.PointId).Should().BeEquivalentTo(new[] { "temperature", "meta" });

        var metaPoint = points.Single(p => p.PointId == "meta");
        metaPoint.Value.Should().BeAssignableTo<Dictionary<string, object?>>();
        var dict = (Dictionary<string, object?>)metaPoint.Value!;
        dict.Should().ContainKey("source");
        dict["source"].Should().Be("sim");
    }
}
