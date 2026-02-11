using System.Text.Json;
using System.Text.Json.Serialization;
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

            return result?.Nodes
                ?? result?.Items
                ?? new List<GraphNodeDto>();
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

            return result?.Nodes
                ?? result?.Items
                ?? new List<GraphNodeDto>();
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

            return result?.Nodes
                ?? result?.Items
                ?? new List<GraphNodeDto>();
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

public record RegistryResponse(
    List<GraphNodeDto>? Nodes,
    List<GraphNodeDto>? Items,
    string? ExportUrl);

public record GraphNodeDto(
    string NodeId,
    [property: JsonConverter(typeof(GraphNodeTypeStringConverter))] string NodeType,
    string? Name,
    string? DisplayName,
    Dictionary<string, object>? Attributes);

public sealed class GraphNodeTypeStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Number => ConvertNumberToName(reader),
            _ => string.Empty
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);

    private static string ConvertNumberToName(Utf8JsonReader reader)
    {
        if (reader.TryGetInt32(out var number))
        {
            return number switch
            {
                1 => "Site",
                2 => "Building",
                3 => "Level",
                4 => "Area",
                5 => "Equipment",
                6 => "Device",
                7 => "Point",
                _ => number.ToString()
            };
        }

        return string.Empty;
    }
}
