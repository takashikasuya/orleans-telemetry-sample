namespace Publisher;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class EmulatorProfileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static EmulatorProfile? TryLoad(string[] args)
    {
        var profileFile = GetArgValue(args, "--profile-file");
        var profileName = GetArgValue(args, "--profile") ?? Environment.GetEnvironmentVariable("PUBLISH_PROFILE");

        if (string.IsNullOrWhiteSpace(profileFile) && string.IsNullOrWhiteSpace(profileName))
        {
            return null;
        }

        var path = profileFile;
        if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(profileName))
        {
            path = Path.Combine(AppContext.BaseDirectory, "profiles", $"{profileName}.json");
            if (!File.Exists(path))
            {
                path = Path.Combine(Directory.GetCurrentDirectory(), "profiles", $"{profileName}.json");
            }
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException($"Profile file was not found. profile={profileName}, profile-file={profileFile}, resolved={path}");
        }

        using var stream = File.OpenRead(path);
        var dto = JsonSerializer.Deserialize<EmulatorProfileDto>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Profile file '{path}' is empty or invalid.");

        return ToModel(dto, profileName ?? dto.Name ?? Path.GetFileNameWithoutExtension(path));
    }

    private static EmulatorProfile ToModel(EmulatorProfileDto dto, string name)
    {
        var devices = dto.Devices?.Select(device =>
            new EmulatorDeviceProfile(
                device.DeviceId ?? throw new InvalidDataException("deviceId is required."),
                (device.Points ?? Enumerable.Empty<EmulatorPointDto>()).Select(point =>
                    new EmulatorPointProfile(
                        point.Id ?? throw new InvalidDataException("point.id is required."),
                        point.Type ?? "number",
                        point.Generator ?? "random",
                        point.MinValue,
                        point.MaxValue,
                        point.Unit,
                        point.Writable ?? false)).ToArray())).ToArray()
            ?? Array.Empty<EmulatorDeviceProfile>();

        if (devices.Length == 0)
        {
            throw new InvalidDataException("Profile must include at least one device.");
        }

        return new EmulatorProfile(
            Name: name,
            Schema: dto.Schema,
            TenantId: dto.TenantId,
            Site: dto.Site is null ? null : new SiteProfile(dto.Site.BuildingName, dto.Site.SpaceId),
            Devices: devices,
            Timing: dto.Timing is null ? null : new TimingProfile(dto.Timing.IntervalMs),
            RandomSeed: dto.RandomSeed);
    }

    private static string? GetArgValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private sealed class EmulatorProfileDto
    {
        public string? Name { get; init; }
        public string? Schema { get; init; }
        public string? TenantId { get; init; }
        public SiteDto? Site { get; init; }
        public TimingDto? Timing { get; init; }
        public int? RandomSeed { get; init; }
        public List<EmulatorDeviceDto>? Devices { get; init; }
    }

    private sealed class SiteDto
    {
        public string? BuildingName { get; init; }
        public string? SpaceId { get; init; }
    }

    private sealed class TimingDto
    {
        public int? IntervalMs { get; init; }
    }

    private sealed class EmulatorDeviceDto
    {
        public string? DeviceId { get; init; }
        public List<EmulatorPointDto>? Points { get; init; }
    }

    private sealed class EmulatorPointDto
    {
        public string? Id { get; init; }
        public string? Type { get; init; }
        public string? Generator { get; init; }
        [JsonPropertyName("min")]
        public double? MinValue { get; init; }
        [JsonPropertyName("max")]
        public double? MaxValue { get; init; }
        public string? Unit { get; init; }
        public bool? Writable { get; init; }
    }
}
