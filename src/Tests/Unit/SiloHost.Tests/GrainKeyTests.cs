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
        var key = PointGrainKey.Create("tenant:a", "point:1");

        key.Should().Be("tenant_a:point_1");
    }
}
