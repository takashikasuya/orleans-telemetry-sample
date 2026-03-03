namespace Grains.Abstractions;

/// <summary>
/// Builds grain keys for point control grains.
/// </summary>
public static class PointControlGrainKey
{
    /// <summary>
    /// Creates a composite key from tenant, device, and point identifiers.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="deviceId">Device identifier.</param>
    /// <param name="pointId">Point identifier.</param>
    /// <returns>Normalized composite grain key.</returns>
    public static string Create(string tenantId, string deviceId, string pointId)
    {
        return string.Join(":", new[]
        {
            NormalizePart(tenantId),
            NormalizePart(deviceId),
            NormalizePart(pointId)
        });
    }

    private static string NormalizePart(string value)
    {
        return (value ?? string.Empty).Replace(":", "_");
    }
}
