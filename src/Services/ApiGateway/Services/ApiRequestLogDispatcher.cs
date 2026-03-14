using System.Text.Json;
using Microsoft.Extensions.Options;
using Telemetry.Ingest;

namespace ApiGateway.Services;

public sealed class ApiRequestLogDispatcher
{
    private static long _sequence;

    private readonly IReadOnlyList<ITelemetryEventSink> _sinks;
    private readonly ApiRequestLoggingOptions _options;
    private readonly ILogger<ApiRequestLogDispatcher> _logger;

    public ApiRequestLogDispatcher(
        IEnumerable<ITelemetryEventSink> sinks,
        IOptions<ApiRequestLoggingOptions> options,
        ILogger<ApiRequestLogDispatcher> logger)
    {
        _options = options.Value;
        _logger = logger;

        var enabledSinks = _options.EnabledSinks;
        if (enabledSinks is null || enabledSinks.Length == 0)
        {
            _sinks = sinks.ToList();
            return;
        }

        var enabledSet = new HashSet<string>(enabledSinks, StringComparer.OrdinalIgnoreCase);
        _sinks = sinks.Where(sink => enabledSet.Contains(sink.Name)).ToList();
    }

    public async Task WriteHttpRequestLogAsync(
        string tenantId,
        string method,
        string path,
        int statusCode,
        double durationMs,
        string? traceId,
        string? queryString,
        string? user,
        CancellationToken ct)
    {
        if (!_options.Enabled || _sinks.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var severity = statusCode >= 500
            ? TelemetryLogSeverity.Error
            : statusCode >= 400
                ? TelemetryLogSeverity.Warning
                : TelemetryLogSeverity.Information;

        var payload = JsonSerializer.SerializeToDocument(new
        {
            method,
            path,
            statusCode,
            durationMs,
            traceId,
            query = queryString,
            user
        });

        var envelope = new TelemetryEventEnvelope(
            tenantId,
            "api-gateway",
            "api-gateway",
            _options.DeviceId,
            _options.PointId,
            Interlocked.Increment(ref _sequence),
            now,
            now,
            TelemetryEventType.Log,
            severity,
            statusCode,
            payload,
            new Dictionary<string, string>
            {
                ["method"] = method,
                ["path"] = path,
                ["status"] = statusCode.ToString(),
                ["tenant"] = tenantId,
                ["traceId"] = traceId ?? string.Empty
            });

        foreach (var sink in _sinks)
        {
            try
            {
                await sink.WriteAsync(envelope, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API request log sink {SinkName} failed.", sink.Name);
            }
        }
    }
}
