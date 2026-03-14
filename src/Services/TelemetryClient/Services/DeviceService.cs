using System.Text.Json;

namespace TelemetryClient.Services;

/// <summary>
/// Service for device-related operations
/// </summary>
public class DeviceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DeviceService> _logger;

    public DeviceService(HttpClient httpClient, ILogger<DeviceService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DeviceSnapshotDto?> GetDeviceAsync(
        string deviceId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/devices/{Uri.EscapeDataString(deviceId)}?tenantId={tenantId}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Device {DeviceId} not found (status: {StatusCode})", deviceId, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<DeviceSnapshotDto>(content, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch device {DeviceId}", deviceId);
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public record DeviceSnapshotDto(
    string DeviceId,
    string? Name,
    Dictionary<string, PointDto> Points,
    DateTimeOffset LastUpdated);

public record PointDto(
    string PointId,
    string? PointType,
    object? Value,
    DateTimeOffset? Timestamp,
    string? Unit,
    bool Writable);
