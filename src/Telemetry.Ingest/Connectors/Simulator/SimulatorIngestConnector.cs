using System.Threading.Channels;
using Grains.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telemetry.Ingest;

namespace Telemetry.Ingest.Simulator;

public sealed class SimulatorIngestConnector : ITelemetryIngestConnector
{
    private readonly SimulatorIngestOptions _options;
    private readonly ILogger<SimulatorIngestConnector> _logger;
    private readonly Random _random = new();

    public SimulatorIngestConnector(
        IOptions<SimulatorIngestOptions> options,
        ILogger<SimulatorIngestConnector> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "Simulator";

    public async Task StartAsync(ChannelWriter<TelemetryPointMsg> writer, CancellationToken ct)
    {
        var deviceCount = Math.Max(1, _options.DeviceCount);
        var pointsPerDevice = Math.Max(1, _options.PointsPerDevice);
        var sequences = new long[deviceCount];
        var delay = TimeSpan.FromMilliseconds(Math.Max(10, _options.IntervalMilliseconds));

        while (!ct.IsCancellationRequested)
        {
            var timestamp = DateTimeOffset.UtcNow;
            for (var deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
            {
                sequences[deviceIndex]++;
                var deviceId = $"{_options.DeviceIdPrefix}{deviceIndex + 1}";
                for (var pointIndex = 0; pointIndex < pointsPerDevice; pointIndex++)
                {
                    var pointId = $"p{pointIndex + 1}";
                    var value = Math.Round(_random.NextDouble() * 100.0, 3);
                    var msg = new TelemetryPointMsg
                    {
                        TenantId = _options.TenantId,
                        BuildingName = _options.BuildingName,
                        SpaceId = _options.SpaceId,
                        DeviceId = deviceId,
                        PointId = pointId,
                        Sequence = sequences[deviceIndex],
                        Timestamp = timestamp,
                        Value = value
                    };
                    await writer.WriteAsync(msg, ct);
                }
            }

            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Simulator ingest connector stopped.");
    }
}
