using System.Text.Json;
using ApiGateway.Contracts;

namespace TelemetryClient.Services;

/// <summary>
/// Service for interacting with ApiGateway registry endpoints
/// </summary>
public class RegistryService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RegistryService> _logger;

    public RegistryService(HttpClient httpClient, ILogger<RegistryService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<GraphNodeDto>> GetSitesAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/registry/sites?tenantId={tenantId}", cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<RegistryResponse>(content, JsonOptions);
            
            return result?.Nodes ?? new List<GraphNodeDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch sites for tenant {TenantId}", tenantId);
            return new List<GraphNodeDto>();
        }
    }

    public async Task<List<GraphNodeDto>> GetBuildingsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/registry/buildings?tenantId={tenantId}", cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<RegistryResponse>(content, JsonOptions);
            
            return result?.Nodes ?? new List<GraphNodeDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch buildings for tenant {TenantId}", tenantId);
            return new List<GraphNodeDto>();
        }
    }

    public async Task<List<GraphNodeDto>> GetDevicesAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/registry/devices?tenantId={tenantId}", cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<RegistryResponse>(content, JsonOptions);
            
            return result?.Nodes ?? new List<GraphNodeDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch devices for tenant {TenantId}", tenantId);
            return new List<GraphNodeDto>();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public record RegistryResponse(List<GraphNodeDto>? Nodes, string? ExportUrl);

public record GraphNodeDto(
    string NodeId,
    string NodeType,
    string? Name,
    Dictionary<string, object>? Attributes);
