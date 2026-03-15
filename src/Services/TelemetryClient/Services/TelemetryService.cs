using System.Text.Json;

namespace TelemetryClient.Services;

/// <summary>
/// Service for telemetry data retrieval
/// </summary>
public class TelemetryService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(HttpClient httpClient, ILogger<TelemetryService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<TelemetryQueryResult> QueryAsync(
        string deviceId,
        string tenantId,
        int limit = 100,
        DateTimeOffset? fallBackTo = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/api/telemetry/{Uri.EscapeDataString(deviceId)}?tenantId={tenantId}&limit={limit}";
            if (fallBackTo.HasValue)
            {
                url += $"&fallBackTo={fallBackTo.Value:O}";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<TelemetryQueryResult>(content, JsonOptions);

            return result ?? new TelemetryQueryResult(new List<TelemetryRecord>(), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query telemetry for device {DeviceId}", deviceId);
            return new TelemetryQueryResult(new List<TelemetryRecord>(), null);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public record TelemetryQueryResult(List<TelemetryRecord> Records, string? ExportUrl);

public record TelemetryRecord(
    string DeviceId,
    string PointId,
    object? Value,
    DateTimeOffset Timestamp,
    Dictionary<string, object>? Metadata);
