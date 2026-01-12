using System.Text.Json.Serialization;
using Telemetry.Storage;

namespace ApiGateway.Telemetry;

public sealed record TelemetryQueryResponse
{
    public string Mode { get; init; } = "inline";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TelemetryQueryResult>? Items { get; init; }

    public int Count { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; init; }

    public static TelemetryQueryResponse Inline(IReadOnlyList<TelemetryQueryResult> items)
        => new()
        {
            Mode = "inline",
            Items = items,
            Count = items.Count
        };

    public static TelemetryQueryResponse UrlResult(string url, DateTimeOffset expiresAt, int count)
        => new()
        {
            Mode = "url",
            Url = url,
            ExpiresAt = expiresAt,
            Count = count
        };
}
