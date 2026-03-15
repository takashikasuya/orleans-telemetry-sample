namespace Grains.Abstractions;

/// <summary>
/// Builds grain keys for device control index grains.
/// </summary>
public static class DeviceControlIndexGrainKey
{
    /// <summary>
    /// Creates a composite key from tenant and device identifiers.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="deviceId">Device identifier.</param>
    /// <returns>Normalized composite grain key.</returns>
    public static string Create(string tenantId, string deviceId)
    {
        return string.Join(":", new[]
        {
            NormalizePart(tenantId),
            NormalizePart(deviceId)
        });
    }

    private static string NormalizePart(string value)
    {
        return (value ?? string.Empty).Replace(":", "_");
    }
}
