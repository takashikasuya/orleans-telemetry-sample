namespace Grains.Abstractions;

/// <summary>
/// Utility methods for constructing grain keys for point grains.
/// </summary>
public static class PointGrainKey
{
    /// <summary>
    /// Creates the storage key for a point grain using the tenant, building, space, device, and point identifiers.
    /// </summary>
    public static string Create(string tenantId, string buildingName, string spaceId, string deviceId, string pointId)
    {
        return string.Join(":", new[]
        {
            NormalizePart(tenantId),
            NormalizePart(buildingName),
            NormalizePart(spaceId),
            NormalizePart(deviceId),
            NormalizePart(pointId)
        });
    }

    private static string NormalizePart(string value)
    {
        return (value ?? string.Empty).Replace(":", "_");
    }
}
