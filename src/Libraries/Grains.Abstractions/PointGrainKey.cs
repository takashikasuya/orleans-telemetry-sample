namespace Grains.Abstractions;

/// <summary>
/// Utility methods for constructing grain keys for point grains.
/// </summary>
public static class PointGrainKey
{
    /// <summary>
    /// Creates the storage key for a point grain using the tenant and point identifiers.
    /// </summary>
    public static string Create(string tenantId, string pointId)
    {
        return string.Join(":", new[]
        {
            NormalizePart(tenantId),
            NormalizePart(pointId)
        });
    }

    private static string NormalizePart(string value)
    {
        return (value ?? string.Empty).Replace(":", "_");
    }
}
