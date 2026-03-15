using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Telemetry.Storage;

public sealed class TelemetryStorageBackgroundService : BackgroundService
{
    private readonly TelemetryStorageCompactor _compactor;
    private readonly TelemetryStorageOptions _options;
    private readonly ILogger<TelemetryStorageBackgroundService> _logger;

    public TelemetryStorageBackgroundService(
        TelemetryStorageCompactor compactor,
        IOptions<TelemetryStorageOptions> options,
        ILogger<TelemetryStorageBackgroundService> logger)
    {
        _compactor = compactor;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.CompactionIntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var count = await _compactor.CompactAsync(stoppingToken);
                if (count > 0)
                {
                    _logger.LogInformation("Compacted {Count} telemetry stage files.", count);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telemetry compaction failed.");
            }
        }
    }
}
