using System.Threading.Channels;
using Grains.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Telemetry.Ingest;

public sealed class TelemetryIngestCoordinator : BackgroundService
{
    private readonly IReadOnlyList<ITelemetryIngestConnector> _connectors;
    private readonly ITelemetryRouterGrain _router;
    private readonly TelemetryIngestOptions _options;
    private readonly ILogger<TelemetryIngestCoordinator> _logger;
    private readonly Channel<TelemetryPointMsg> _channel;

    public TelemetryIngestCoordinator(
        IEnumerable<ITelemetryIngestConnector> connectors,
        ITelemetryRouterGrain router,
        IOptions<TelemetryIngestOptions> options,
        ILogger<TelemetryIngestCoordinator> logger)
    {
        _connectors = connectors.ToList();
        _router = router;
        _options = options.Value;
        _logger = logger;
        var capacity = Math.Max(1, _options.ChannelCapacity);
        _channel = Channel.CreateBounded<TelemetryPointMsg>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _options.Enabled ?? Array.Empty<string>();
        var enabledSet = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);
        var activeConnectors = enabledSet.Count == 0
            ? _connectors
            : _connectors.Where(c => enabledSet.Contains(c.Name)).ToList();

        if (activeConnectors.Count == 0)
        {
            _logger.LogWarning("No telemetry ingest connectors enabled.");
            _channel.Writer.TryComplete();
            return;
        }

        var connectorTasks = activeConnectors.Select(c => RunConnectorAsync(c, stoppingToken)).ToArray();
        var routeTask = RouteLoopAsync(stoppingToken);

        try
        {
            await Task.WhenAll(connectorTasks);
        }
        finally
        {
            _channel.Writer.TryComplete();
        }

        await routeTask;
    }

    private async Task RunConnectorAsync(ITelemetryIngestConnector connector, CancellationToken ct)
    {
        try
        {
            await connector.StartAsync(_channel.Writer, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telemetry connector {ConnectorName} stopped with error.", connector.Name);
        }
    }

    private async Task RouteLoopAsync(CancellationToken ct)
    {
        var batchSize = Math.Max(1, _options.BatchSize);
        var batch = new List<TelemetryPointMsg>(batchSize);
        try
        {
            await foreach (var msg in _channel.Reader.ReadAllAsync(ct))
            {
                batch.Add(msg);
                if (batch.Count < batchSize)
                {
                    continue;
                }

                await _router.RouteBatchAsync(batch.ToArray());
                batch.Clear();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (batch.Count > 0)
            {
                await _router.RouteBatchAsync(batch.ToArray());
                batch.Clear();
            }
        }
    }
}
