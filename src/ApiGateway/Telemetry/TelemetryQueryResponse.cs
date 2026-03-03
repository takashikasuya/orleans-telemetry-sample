using System.Text.Json.Serialization;
using Telemetry.Storage;

namespace ApiGateway.Telemetry;

/// <summary>
/// Represents response payload for telemetry query endpoints.
/// </summary>
public sealed record TelemetryQueryResponse
{
    /// <summary>
    /// Gets the response mode (`inline` or `url`).
    /// </summary>
    public string Mode { get; init; } = "inline";

    /// <summary>
    /// Gets inline query items when mode is `inline`.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TelemetryQueryResult>? Items { get; init; }

    /// <summary>
    /// Gets the item count represented by this response.
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// Gets download URL when mode is `url`.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; init; }

    /// <summary>
    /// Gets export expiration timestamp when mode is `url`.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Creates an inline response.
    /// </summary>
    /// <param name="items">Inline telemetry items.</param>
    /// <returns>Inline query response.</returns>
    public static TelemetryQueryResponse Inline(IReadOnlyList<TelemetryQueryResult> items)
        => new()
        {
            Mode = "inline",
            Items = items,
            Count = items.Count
        };

    /// <summary>
    /// Creates a URL-based response.
    /// </summary>
    /// <param name="url">Export URL.</param>
    /// <param name="expiresAt">Export expiration timestamp.</param>
    /// <param name="count">Result count.</param>
    /// <returns>URL-based query response.</returns>
    public static TelemetryQueryResponse UrlResult(string url, DateTimeOffset expiresAt, int count)
        => new()
        {
            Mode = "url",
            Url = url,
            ExpiresAt = expiresAt,
            Count = count
        };
}
