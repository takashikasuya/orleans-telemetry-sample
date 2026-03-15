using System;
using System.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiGateway.Services;

internal sealed class RegistryExportCleanupService : BackgroundService
{
    private readonly RegistryExportService _exports;
    private readonly RegistryExportOptions _options;
    private readonly ILogger<RegistryExportCleanupService> _logger;

    public RegistryExportCleanupService(
        RegistryExportService exports,
        IOptions<RegistryExportOptions> options,
        ILogger<RegistryExportCleanupService> logger)
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
                    _logger.LogInformation("Removed {Count} expired registry exports.", count);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registry export cleanup failed.");
            }
        }
    }
}
