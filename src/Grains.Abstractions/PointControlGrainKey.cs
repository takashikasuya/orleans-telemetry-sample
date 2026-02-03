namespace Grains.Abstractions;

public static class PointControlGrainKey
{
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
