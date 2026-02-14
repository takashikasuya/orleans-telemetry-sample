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
            _logger.LogInformation("Fetching sites for tenant {TenantId}", tenantId);
            var response = await _httpClient.GetAsync($"/api/registry/sites?tenantId={tenantId}", cancellationToken);
            
            _logger.LogInformation("Registry sites response: {StatusCode}", response.StatusCode);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Registry sites request failed with {StatusCode}: {Content}", response.StatusCode, errorContent);
                return new List<GraphNodeDto>();
            }
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("Registry sites response content: {Content}", content);
            
            var result = JsonSerializer.Deserialize<RegistryQueryResponse>(content, JsonOptions);

            // Handle both inline results and empty results
            if (result?.Items == null || result.Items.Count == 0)
            {
                _logger.LogInformation("Registry returned {Count} sites (result is null: {IsNull})", result?.TotalCount ?? 0, result == null);
                return new List<GraphNodeDto>();
            }

            _logger.LogInformation("Successfully fetched {Count} sites", result.Items.Count);
            
            // Convert RegistryNodeSummary to GraphNodeDto
            return result.Items.Select(item => new GraphNodeDto(
                item.NodeId,
                item.NodeType,
                item.DisplayName, // Use DisplayName as Name
                item.DisplayName,
                item.Attributes?.ToDictionary(k => k.Key, k => (object)k.Value)
            )).ToList();
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
            var result = JsonSerializer.Deserialize<RegistryQueryResponse>(content, JsonOptions);

            if (result?.Items == null || result.Items.Count == 0)
            {
                return new List<GraphNodeDto>();
            }

            return result.Items.Select(item => new GraphNodeDto(
                item.NodeId,
                item.NodeType,
                item.DisplayName,
                item.DisplayName,
                item.Attributes?.ToDictionary(k => k.Key, k => (object)k.Value)
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch buildings for tenant {TenantId}", tenantId);
            return new List<GraphNodeDto>();
        }
    }
QueryResponse>(content, JsonOptions);

            if (result?.Items == null || result.Items.Count == 0)
            {
                return new List<GraphNodeDto>();
            }

            return result.Items.Select(item => new GraphNodeDto(
                item.NodeId,
                item.NodeType,
                item.DisplayName,
                item.DisplayName,
                item.Attributes?.ToDictionary(k => k.Key, k => (object)k.Value)
            )).ToList
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
// API response types matching ApiGateway's RegistryQueryResponse
public record RegistryQueryResponse(
    string Mode,
    int Count,
    int TotalCount,
    List<RegistryNodeSummary>? Items,
    string? Url,
    DateTimeOffset? ExpiresAt);

public record RegistryNodeSummary(
    string NodeId,
    string NodeType,
    string DisplayName,
    IReadOnlyDictionary<string, string>? Attributesadonly JsonSerializerOptions JsonOptions = new()
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
