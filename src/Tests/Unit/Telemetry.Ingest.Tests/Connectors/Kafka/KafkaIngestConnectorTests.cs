using System.Text;
using System.Text.Json;
using FluentAssertions;
using Grains.Abstractions;
using Telemetry.Ingest.Kafka;
using Xunit;

namespace Telemetry.Ingest.Tests.Connectors.Kafka;

public sealed class KafkaIngestConnectorTests
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
            Sequence = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Properties = new Dictionary<string, object>
            {
                ["p1"] = 10.2
            }
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);

        var ok = KafkaIngestConnector.TryDeserializeTelemetry(bytes, out var message, out var exception);

        ok.Should().BeTrue();
        exception.Should().BeNull();
        message.Should().NotBeNull();
        message!.Properties.Should().ContainKey("p1");
    }

    [Fact]
    public void TryDeserializeTelemetry_ReturnsFalse_ForInvalidPayload()
    {
        var bytes = Encoding.UTF8.GetBytes("bad-json");

        var ok = KafkaIngestConnector.TryDeserializeTelemetry(bytes, out var message, out var exception);

        ok.Should().BeFalse();
        message.Should().BeNull();
        exception.Should().NotBeNull();
    }

    [Fact]
    public void ToTelemetryPointMessages_MapsAllProperties()
    {
        var message = new TelemetryMsg
        {
            TenantId = "t1",
            BuildingName = "b1",
            SpaceId = "s1",
            DeviceId = "d1",
            Sequence = 77,
            Timestamp = DateTimeOffset.UtcNow,
            Properties = new Dictionary<string, object>
            {
                ["p1"] = 100,
                ["p2"] = "on"
            }
        };

        var points = KafkaIngestConnector.ToTelemetryPointMessages(message);

        points.Should().HaveCount(2);
        points.Should().ContainSingle(p => p.PointId == "p1" && (int)p.Value! == 100);
        points.Should().ContainSingle(p => p.PointId == "p2" && (string)p.Value! == "on");
    }
}
