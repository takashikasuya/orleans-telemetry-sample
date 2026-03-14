using System;
using System.IO;
using System.Linq;
using Publisher;
using Xunit;

namespace Publisher.Tests;

public class ProfileTelemetryGeneratorTests
{
    [Fact]
    public void TryLoadReturnsNullWhenNoProfileOptions()
    {
        var profile = EmulatorProfileLoader.TryLoad(Array.Empty<string>());
        Assert.Null(profile);
    }

    [Fact]
    public void TryLoadProfileFileParsesDevicesAndPoints()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
            {
              "name": "bacnet-office-v1",
              "schema": "bacnet-normalized-v1",
              "tenantId": "t-profile",
              "site": { "buildingName": "b-profile", "spaceId": "s-profile" },
              "timing": { "intervalMs": 250 },
              "randomSeed": 7,
              "devices": [
                {
                  "deviceId": "ahu-01",
                  "points": [
                    { "id": "supply_air_temp", "type": "number", "generator": "sin", "min": 16, "max": 30, "writable": true },
                    { "id": "fan_status", "type": "bool", "generator": "step" }
                  ]
                }
              ]
            }
            """);

            var profile = EmulatorProfileLoader.TryLoad(new[] { "--profile-file", path });

            Assert.NotNull(profile);
            Assert.Equal("bacnet-office-v1", profile!.Name);
            Assert.Equal("t-profile", profile.TenantId);
            Assert.Equal("b-profile", profile.Site?.BuildingName);
            Assert.Equal(250, profile.Timing?.IntervalMs);
            Assert.Single(profile.Devices);
            Assert.Equal("ahu-01", profile.Devices[0].DeviceId);
            Assert.Equal(2, profile.Devices[0].Points.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GeneratorProducesTelemetryForProfilePoints()
    {
        var profile = new EmulatorProfile(
            Name: "test",
            Schema: "bacnet-normalized-v1",
            TenantId: "t1",
            Site: new SiteProfile("b1", "s1"),
            Devices: new[]
            {
                new EmulatorDeviceProfile(
                    "ahu-01",
                    new[]
                    {
                        new EmulatorPointProfile("temp", "number", "constant", 10, 20, "degC", true),
                        new EmulatorPointProfile("status", "bool", "step", null, null, null, false)
                    })
            },
            Timing: new TimingProfile(1000),
            RandomSeed: 1);

        var generator = new ProfileTelemetryGenerator(profile);
        var msg = generator.CreateTelemetry("t1", "b1", "s1", profile.Devices.Single(), 10);

        Assert.Equal("ahu-01", msg.DeviceId);
        Assert.Equal(10, msg.Sequence);
        Assert.True(msg.Properties.ContainsKey("temp"));
        Assert.True(msg.Properties.ContainsKey("status"));
        Assert.True(msg.Properties["temp"] is long or double);
        Assert.IsType<bool>(msg.Properties["status"]);

        Assert.Equal("b1", msg.BuildingName);
        Assert.Equal("s1", msg.SpaceId);
    }
}
