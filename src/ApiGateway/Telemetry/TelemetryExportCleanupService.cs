using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiGateway.Telemetry;

public sealed class TelemetryExportCleanupService : BackgroundService
{
    private readonly TelemetryExportService _exports;
    private readonly TelemetryExportOptions _options;
    private readonly ILogger<TelemetryExportCleanupService> _logger;

    public TelemetryExportCleanupService(
        TelemetryExportService exports,
        IOptions<TelemetryExportOptions> options,
        ILogger<TelemetryExportCleanupService> logger)
    {
        _exports = exports;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.CleanupIntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var count = await _exports.CleanupExpiredAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (count > 0)
                {
                    _logger.LogInformation("Removed {Count} expired telemetry exports.", count);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telemetry export cleanup failed.");
            }
        }
    }
}
