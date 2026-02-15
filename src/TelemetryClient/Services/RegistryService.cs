using System.Text.Json;
using System.Text.Json.Serialization;

namespace TelemetryClient.Services;

/// <summary>
/// Service for interacting with ApiGateway registry endpoints.
/// </summary>
public class RegistryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<RegistryService> _logger;

    public RegistryService(HttpClient httpClient, ILogger<RegistryService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<List<GraphNodeDto>> GetSitesAsync(string tenantId, CancellationToken cancellationToken = default)
        => GetNodesAsync("sites", tenantId, cancellationToken);

    public Task<List<GraphNodeDto>> GetBuildingsAsync(string tenantId, CancellationToken cancellationToken = default)
        => GetNodesAsync("buildings", tenantId, cancellationToken);

    public Task<List<GraphNodeDto>> GetDevicesAsync(string tenantId, CancellationToken cancellationToken = default)
        => GetNodesAsync("devices", tenantId, cancellationToken);

    private async Task<List<GraphNodeDto>> GetNodesAsync(
        string endpoint,
        string tenantId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Fetching {Endpoint} for tenant {TenantId}", endpoint, tenantId);

            var response = await _httpClient.GetAsync($"/api/registry/{endpoint}?tenantId={tenantId}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Registry {Endpoint} request failed with {StatusCode}: {Content}",
                    endpoint,
                    response.StatusCode,
                    errorContent);
                return [];
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<RegistryQueryResponse>(content, JsonOptions);

            if (result?.Items is not { Count: > 0 })
            {
                _logger.LogInformation(
                    "Registry returned no {Endpoint} for tenant {TenantId} (total: {TotalCount})",
                    endpoint,
                    tenantId,
                    result?.TotalCount ?? 0);
                return [];
            }

            return result.Items.Select(item => new GraphNodeDto(
                item.NodeId,
                item.NodeType,
                item.DisplayName,
                item.DisplayName,
                item.Attributes?.ToDictionary(static kvp => kvp.Key, static kvp => (object)kvp.Value)))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch {Endpoint} for tenant {TenantId}", endpoint, tenantId);
            return [];
        }
    }
}

public record RegistryQueryResponse(
    string Mode,
    int Count,
    int TotalCount,
    List<RegistryNodeSummary>? Items,
    string? Url,
    DateTimeOffset? ExpiresAt);

public record RegistryNodeSummary(
    string NodeId,
    [property: JsonConverter(typeof(GraphNodeTypeStringConverter))] string NodeType,
    string DisplayName,
    IReadOnlyDictionary<string, string>? Attributes);

public record GraphNodeDto(
    string NodeId,
    string NodeType,
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
