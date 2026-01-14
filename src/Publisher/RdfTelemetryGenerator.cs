using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DataModel.Analyzer.Models;
using Grains.Abstractions;

namespace Publisher;

internal sealed class RdfTelemetryGenerator
{
    internal const string MetadataKey = "_pointMetadata";
    private static readonly string[] BooleanKeywords =
    {
        "binary",
        "boolean",
        "bool",
        "switch",
        "state",
        "occupancy",
        "alarm",
        "door",
        "override",
        "onoff"
    };

    private readonly List<RdfDeviceDefinition> _devices;
    private readonly Random _random;

    public RdfTelemetryGenerator(BuildingDataModel model, Random? random = null)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        _random = random ?? new Random();
        _devices = BuildDevices(model).ToList();
    }

    public IReadOnlyList<RdfDeviceDefinition> Devices => _devices;

    public int DeviceCount => _devices.Count;

    public int PointCount => _devices.Sum(d => d.Points.Count);

    public TelemetryMsg CreateTelemetry(string tenantId, RdfDeviceDefinition device, long sequence)
    {
        var properties = new Dictionary<string, object>();
        var metadata = new Dictionary<string, PointMetadata>();

        foreach (var point in device.Points)
        {
            var value = GenerateValue(point);
            properties[point.PointId] = value;
            metadata[point.PointId] = new PointMetadata(
                PointType: point.PointType,
                Unit: point.Unit,
                MinValue: point.MinPresValue,
                MaxValue: point.MaxPresValue,
                Writable: point.Writable);
        }

        if (metadata.Count > 0)
        {
            properties[MetadataKey] = metadata;
        }

        return new TelemetryMsg(
            TenantId: tenantId,
            DeviceId: device.DeviceId,
            Sequence: sequence,
            Timestamp: DateTimeOffset.UtcNow,
            Properties: properties,
            BuildingName: device.BuildingName,
            SpaceId: device.SpaceId);
    }

    private object GenerateValue(RdfPointDefinition point)
    {
        if (IsBoolean(point.Source))
        {
            return _random.Next(2) == 0;
        }

        var (min, max) = ResolveRange(point);
        var raw = min + _random.NextDouble() * (max - min);
        return Math.Round(raw, 3);
    }

    private static (double Min, double Max) ResolveRange(RdfPointDefinition point)
    {
        var min = point.MinPresValue ?? (point.MaxPresValue.HasValue ? point.MaxPresValue.Value - 10 : 0);
        var max = point.MaxPresValue ?? (point.MinPresValue.HasValue ? point.MinPresValue.Value + 10 : 100);
        if (max <= min)
        {
            max = min + 10;
        }

        return (min, max);
    }

    private static bool IsBoolean(Point point)
    {
        var candidates = new[]
        {
            point.PointType,
            point.Name,
            point.PointSpecification,
            point.LocalId
        };

        return candidates.Any(value =>
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.ToLowerInvariant();
            return BooleanKeywords.Any(keyword => normalized.Contains(keyword));
        });
    }

    private static IReadOnlyList<RdfDeviceDefinition> BuildDevices(BuildingDataModel model)
    {
        var devices = new List<RdfDeviceDefinition>();

        foreach (var equipment in model.Equipment)
        {
            var definition = BuildDefinition(model, equipment);
            if (definition is not null)
            {
                devices.Add(definition);
            }
        }

        return devices;
    }

    private static RdfDeviceDefinition? BuildDefinition(BuildingDataModel model, Equipment equipment)
    {
        var pointDefinitions = new List<RdfPointDefinition>();
        var usedPointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var point in equipment.Points)
        {
            var basePointId = ResolvePointId(point);
            if (string.IsNullOrWhiteSpace(basePointId))
            {
                continue;
            }

            var uniquePointId = basePointId;
            var attempt = 1;
            while (usedPointIds.Contains(uniquePointId))
            {
                uniquePointId = $"{basePointId}-{attempt}";
                attempt++;
            }

            usedPointIds.Add(uniquePointId);
            pointDefinitions.Add(new RdfPointDefinition(point, uniquePointId));
        }

        if (pointDefinitions.Count == 0)
        {
            return null;
        }

        var deviceId = ResolveDeviceId(equipment);
        var (buildingName, spaceId) = ResolveLocation(model, equipment, deviceId);
        return new RdfDeviceDefinition(deviceId, buildingName, spaceId, pointDefinitions);
    }

    private static string ResolveDeviceId(Equipment equipment)
    {
        if (!string.IsNullOrWhiteSpace(equipment.DeviceId))
        {
            return equipment.DeviceId;
        }

        if (!string.IsNullOrWhiteSpace(equipment.Name))
        {
            return NormalizeId(equipment.Name);
        }

        if (!string.IsNullOrWhiteSpace(equipment.Uri))
        {
            return NormalizeId(equipment.Uri);
        }

        return $"equipment-{Math.Abs(equipment.GetHashCode()):X}";
    }

    private static string ResolvePointId(Point point)
    {
        if (!string.IsNullOrWhiteSpace(point.PointId))
        {
            return point.PointId;
        }

        if (!string.IsNullOrWhiteSpace(point.LocalId))
        {
            return NormalizeId(point.LocalId);
        }

        if (!string.IsNullOrWhiteSpace(point.Name))
        {
            return NormalizeId(point.Name);
        }

        if (!string.IsNullOrWhiteSpace(point.Uri))
        {
            return NormalizeId(point.Uri);
        }

        return string.Empty;
    }

    private static (string BuildingName, string SpaceId) ResolveLocation(BuildingDataModel model, Equipment equipment, string defaultId)
    {
        var area = model.Areas.FirstOrDefault(a => a.Uri == equipment.AreaUri);
        var level = area is not null ? model.Levels.FirstOrDefault(l => l.Uri == area.LevelUri) : null;
        var building = level is not null ? model.Buildings.FirstOrDefault(b => b.Uri == level.BuildingUri) : null;

        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(building?.Name))
        {
            segments.Add(building.Name);
        }

        if (!string.IsNullOrWhiteSpace(level?.Name))
        {
            segments.Add(level.Name);
        }

        if (!string.IsNullOrWhiteSpace(area?.Name))
        {
            segments.Add(area.Name);
        }

        var spaceId = segments.Count > 0 ? string.Join("/", segments) : defaultId;
        var buildingName = building?.Name ?? level?.Name ?? area?.Name ?? equipment.Name ?? defaultId;
        return (buildingName, spaceId);
    }

    private static string NormalizeId(string value)
    {
        var normalized = value.ToLowerInvariant();
        normalized = normalized.Replace(" ", "-", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"[^a-z0-9\-]", "-");
        normalized = Regex.Replace(normalized, "-{2,}", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? Guid.NewGuid().ToString("N") : normalized;
    }

    internal sealed record RdfDeviceDefinition(string DeviceId, string BuildingName, string SpaceId, IReadOnlyList<RdfPointDefinition> Points);

    internal sealed record RdfPointDefinition(Point Source, string PointId)
    {
        public string PointType => Source.PointType ?? string.Empty;
        public string? Unit => Source.Unit;
        public double? MinPresValue => Source.MinPresValue;
        public double? MaxPresValue => Source.MaxPresValue;
        public bool Writable => Source.Writable;
    }

    private sealed record PointMetadata(string PointType, string? Unit, double? MinValue, double? MaxValue, bool Writable);
}
