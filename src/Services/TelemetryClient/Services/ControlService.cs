using System.Net.Http.Json;
using System.Text.Json;
using ApiGateway.Contracts;

namespace TelemetryClient.Services;

/// <summary>
/// Service for device control operations
/// </summary>
public class ControlService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ControlService> _logger;

    public ControlService(HttpClient httpClient, ILogger<ControlService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PointControlResponse?> SubmitControlCommandAsync(
        string deviceId,
        PointControlRequest request,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/api/devices/{Uri.EscapeDataString(deviceId)}/control?tenantId={tenantId}";
            var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Control command failed for device {DeviceId}, point {PointId} (status: {StatusCode})",
                    deviceId, request.PointId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PointControlResponse>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit control command for device {DeviceId}", deviceId);
            return null;
        }
    }
}
