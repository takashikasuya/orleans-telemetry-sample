namespace ApiGateway.Models;

/// <summary>
/// Represents a point value and its update timestamp.
/// </summary>
/// <param name="Value">Latest point value.</param>
/// <param name="UpdatedAt">Timestamp when the value was updated.</param>
public sealed record PointPropertyValue(object? Value, DateTimeOffset UpdatedAt);
