namespace Publisher;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grains.Abstractions;

internal sealed class ProfileTelemetryGenerator
{
    private readonly EmulatorProfile profile;
    private readonly Random random;
    private readonly Dictionary<string, long> stepByPoint = new(StringComparer.OrdinalIgnoreCase);

    public ProfileTelemetryGenerator(EmulatorProfile profile)
    {
        this.profile = profile;
        random = profile.RandomSeed is int seed ? new Random(seed) : new Random();
    }

    public TelemetryMsg CreateTelemetry(string tenantId, string buildingName, string spaceId, EmulatorDeviceProfile device, long sequence)
    {
        var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in device.Points)
        {
            props[point.Id] = NextValue(device.DeviceId, point);
        }

        return new TelemetryMsg(
            TenantId: tenantId,
            DeviceId: device.DeviceId,
            Sequence: sequence,
            Timestamp: DateTimeOffset.UtcNow,
            Properties: props,
            BuildingName: buildingName,
            SpaceId: spaceId);
    }

    private object NextValue(string deviceId, EmulatorPointProfile point)
    {
        var pointKey = $"{deviceId}:{point.Id}";
        var step = stepByPoint.TryGetValue(pointKey, out var current) ? current + 1 : 1;
        stepByPoint[pointKey] = step;

        var generator = point.Generator.ToLowerInvariant();
        var type = point.Type.ToLowerInvariant();
        var min = point.Min ?? 0;
        var max = point.Max ?? (type == "bool" ? 1 : 100);

        return generator switch
        {
            "step" => type == "bool" ? (step % 2 == 0) : Clamp(min + (step % Math.Max(1, (int)(max - min + 1))), min, max),
            "sin" => type == "bool" ? Math.Sin(step / 5.0) >= 0 : Clamp(min + ((Math.Sin(step / 5.0) + 1) / 2.0) * (max - min), min, max),
            "constant" => type == "bool" ? max > 0 : NormalizeNumber(max),
            _ => type == "bool" ? random.Next(0, 2) == 1 : Clamp(min + random.NextDouble() * (max - min), min, max)
        };
    }

    private static object NormalizeNumber(double value)
    {
        return Math.Abs(value % 1) < double.Epsilon ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : Math.Round(value, 4);
    }

    private static object Clamp(double value, double min, double max)
    {
        var clamped = Math.Min(max, Math.Max(min, value));
        return NormalizeNumber(clamped);
    }
}
