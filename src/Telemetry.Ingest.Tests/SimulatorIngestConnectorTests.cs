using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telemetry.Ingest.Simulator;
using Xunit;

namespace Telemetry.Ingest.Tests;

public sealed class SimulatorIngestConnectorTests
{
    [Fact]
    public async Task SimulatorConnectorEmitsTelemetryPoints()
    {
        var options = Options.Create(new SimulatorIngestOptions
        {
            TenantId = "tenant-a",
            BuildingName = "b1",
            SpaceId = "s1",
            DeviceIdPrefix = "dev-",
            DeviceCount = 1,
            PointsPerDevice = 2,
            IntervalMilliseconds = 10
        });

        var connector = new SimulatorIngestConnector(
            options,
            NullLogger<SimulatorIngestConnector>.Instance);

        var channel = Channel.CreateUnbounded<Grains.Abstractions.TelemetryPointMsg>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var runTask = connector.StartAsync(channel.Writer, cts.Token);

        var first = await channel.Reader.ReadAsync(cts.Token);
        var second = await channel.Reader.ReadAsync(cts.Token);

        first.TenantId.Should().Be("tenant-a");
        first.DeviceId.Should().Be("dev-1");
        first.PointId.Should().Be("p1");
        second.PointId.Should().Be("p2");

        cts.Cancel();
        await runTask;
    }
}
