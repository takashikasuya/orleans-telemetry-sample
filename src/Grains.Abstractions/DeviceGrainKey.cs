namespace Grains.Abstractions;

/// <summary>
/// Utility methods for constructing grain keys for device grains.
/// </summary>
public static class DeviceGrainKey
{
    /// <summary>
    /// Creates the storage key for a device grain using the tenant and device identifiers.
    /// </summary>
    public static string Create(string tenantId, string deviceId) => $"{tenantId}:{deviceId}";
}
