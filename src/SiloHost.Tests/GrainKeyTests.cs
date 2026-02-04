using FluentAssertions;
using Grains.Abstractions;
using Xunit;

namespace SiloHost.Tests;

public sealed class GrainKeyTests
{
    [Fact]
    public void DeviceGrainKey_IncludesTenant()
    {
        var tenantA = DeviceGrainKey.Create("tenant-a", "device-1");
        var tenantB = DeviceGrainKey.Create("tenant-b", "device-1");

        tenantA.Should().NotBe(tenantB);
        tenantA.Should().Be("tenant-a:device-1");
        tenantB.Should().Be("tenant-b:device-1");
    }

    [Fact]
    public void PointGrainKey_IncludesTenantAndNormalizesParts()
    {
        var key = PointGrainKey.Create("tenant:a", "building:1", "space:1", "device:1", "point:1");

        key.Should().Be("tenant_a:building_1:space_1:device_1:point_1");
    }
}
