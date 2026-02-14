namespace Publisher;

using System.Collections.Generic;

internal sealed record EmulatorProfile(
    string Name,
    string? Schema,
    string? TenantId,
    SiteProfile? Site,
    IReadOnlyList<EmulatorDeviceProfile> Devices,
    TimingProfile? Timing,
    int? RandomSeed);

internal sealed record SiteProfile(string? BuildingName, string? SpaceId);

internal sealed record TimingProfile(int? IntervalMs);

internal sealed record EmulatorDeviceProfile(
    string DeviceId,
    IReadOnlyList<EmulatorPointProfile> Points);

internal sealed record EmulatorPointProfile(
    string Id,
    string Type,
    string Generator,
    double? Min,
    double? Max,
    string? Unit,
    bool Writable);
