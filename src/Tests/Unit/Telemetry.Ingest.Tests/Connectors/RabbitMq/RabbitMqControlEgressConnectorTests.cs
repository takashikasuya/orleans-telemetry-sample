using System.Text.Json;
using FluentAssertions;
using Telemetry.Ingest.RabbitMq;
using Xunit;

namespace Telemetry.Ingest.Tests.Connectors.RabbitMq;

public sealed class RabbitMqControlEgressConnectorTests
{
    [Fact]
    public void Name_Returns_RabbitMq()
    {
        var connector = CreateConnector();
        connector.Name.Should().Be("RabbitMq");
    }

    [Fact]
    public void ConfirmMode_Returns_AckOnly()
    {
        var connector = CreateConnector();
        connector.ConfirmMode.Should().Be(ControlConfirmMode.AckOnly);
    }

    [Fact]
    public void SerializeRequest_ProducesPublisherCompatibleJson()
    {
        var request = new ControlEgressRequest
        {
            CommandId = "cmd-001",
            TenantId = "t1",
            BuildingName = "b1",
            SpaceId = "s1",
            DeviceId = "ahu-01",
            PointId = "setpoint-temp",
            DesiredValue = 22.5,
            RequestedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Metadata = new Dictionary<string, string>
            {
                ["ConnectorName"] = "RabbitMq",
                ["GatewayId"] = "gw-01"
            }
        };

        var bytes = RabbitMqControlEgressConnector.SerializeRequest(request);

        using var document = JsonDocument.Parse(bytes);
        document.RootElement.GetProperty("deviceId").GetString().Should().Be("ahu-01");
        document.RootElement.GetProperty("pointId").GetString().Should().Be("setpoint-temp");
        document.RootElement.GetProperty("value").GetDouble().Should().Be(22.5);
        document.RootElement.GetProperty("commandId").GetString().Should().Be("cmd-001");
        document.RootElement.GetProperty("metadata").GetProperty("ConnectorName").GetString().Should().Be("RabbitMq");
        document.RootElement.TryGetProperty("DesiredValue", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_ReturnsFailure_WhenConnectionFails()
    {
        var connector = CreateConnector(hostname: "localhost-nonexistent-99999");
        using (connector)
        {
            var request = new ControlEgressRequest
            {
                CommandId = "cmd-fail",
                DeviceId = "ahu-01",
                PointId = "setpoint-temp"
            };

            var result = await connector.SendAsync(request, CancellationToken.None);

            result.Should().NotBeNull();
            result.Accepted.Should().BeFalse();
            result.Error.Should().NotBeNullOrEmpty();
            result.CommandId.Should().Be("cmd-fail");
            result.ConnectorName.Should().Be("RabbitMq");
        }
    }

    private static RabbitMqControlEgressConnector CreateConnector(string? hostname = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMqControlEgressOptions
        {
            HostName = hostname,
            Port = 5672,
            UserName = "user",
            Password = "password",
            QueueName = "telemetry-control"
        });

        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<RabbitMqControlEgressConnector>();
        return new RabbitMqControlEgressConnector(options, logger);
    }
}
