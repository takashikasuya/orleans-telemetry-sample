using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DataModel.Analyzer.Models;
using Publisher;
using Xunit;

namespace Publisher.Tests;

public class RdfTelemetryGeneratorTests
{
    [Fact]
    public void GeneratorProducesDeviceFromEquipment()
    {
        var model = CreateModel("TenantDevice", "Room A", "temp", "TemperatureSensor");
        var generator = new RdfTelemetryGenerator(model);

        Assert.Single(generator.Devices);
        var device = generator.Devices.Single();
        Assert.Equal("TenantDevice", device.DeviceId);
        Assert.Equal("Building A/Level 1/Room A", device.SpaceId);
        Assert.Single(device.Points);
        Assert.Equal("temp", device.Points.Single().PointId);
    }

    [Fact]
    public void TelemetryIncludesMetadataAndLimits()
    {
        var model = CreateModel("Device-A", "Main Building/Level 1", "temp", "Temperature");
        model.Points[0].MinPresValue = 10;
        model.Points[0].MaxPresValue = 30;

        var generator = new RdfTelemetryGenerator(model);
        var device = generator.Devices.Single();
        var msg = generator.CreateTelemetry("tenant", device, 1);

        Assert.True(msg.Properties.ContainsKey("temp"));
        var tempValue = Assert.IsType<double>(msg.Properties["temp"]);
        Assert.InRange(tempValue, 10, 30);
        Assert.True(msg.Properties.ContainsKey("_pointMetadata"));

        var metadata = Assert.IsAssignableFrom<IDictionary>(msg.Properties["_pointMetadata"]);
        Assert.True(metadata.Contains("temp"));
        var metadataEntry = metadata["temp"];
        Assert.NotNull(metadataEntry);
        Assert.Equal("Temperature", metadataEntry.GetType().GetProperty("PointType")?.GetValue(metadataEntry));
    }

    [Fact]
    public void BooleanPointYieldsBooleanValue()
    {
        var model = CreateModel("BoolDevice", "Flag Room", "flag", "BinarySwitch");
        var generator = new RdfTelemetryGenerator(model);
        var device = generator.Devices.Single();
        var msg = generator.CreateTelemetry("tenant", device, 5);

        Assert.True(msg.Properties.ContainsKey("flag"));
        Assert.IsType<bool>(msg.Properties["flag"]);
    }

    [Fact]
    public void DeviceIdFallsBackToNormalizedNameWhenMissing()
    {
        var model = CreateModel(string.Empty, "Fallback Space", "status", "BooleanIndicator", deviceName: "My Device 01");
        var generator = new RdfTelemetryGenerator(model);

        Assert.Single(generator.Devices);
        var device = generator.Devices.Single();
        Assert.Equal("my-device-01", device.DeviceId);
    }

    private static BuildingDataModel CreateModel(string deviceId, string spaceName, string pointId, string pointType, string? deviceName = null)
    {
        var site = new Site { Uri = "site-a", Name = "Site A" };
        var building = new Building { Uri = "building-a", SiteUri = site.Uri, Name = "Building A" };
        var level = new Level { Uri = "level-a", BuildingUri = building.Uri, Name = "Level 1" };
        var area = new Area { Uri = "area-a", LevelUri = level.Uri, Name = spaceName };
        var point = new Point
        {
            PointId = pointId,
            PointType = pointType,
            EquipmentUri = "equipment-a"
        };
        var equipment = new Equipment
        {
            AreaUri = area.Uri,
            DeviceId = deviceId,
            Name = deviceName ?? "Managed Device",
            Uri = "equipment-a",
            Points = new List<Point> { point }
        };

        return new BuildingDataModel
        {
            Sites = new List<Site> { site },
            Buildings = new List<Building> { building },
            Levels = new List<Level> { level },
            Areas = new List<Area> { area },
            Equipment = new List<Equipment> { equipment },
            Points = new List<Point> { point }
        };
    }
}
