using System;
using FluentAssertions;
using Grains.Abstractions;
using Xunit;

namespace SiloHost.Tests;

public sealed class DeviceGrainTests
{
    [Fact]
    public async Task UpsertAsync_MergesPropertiesAndUpdatesSequence()
    {
        var state = new TestPersistentState<DeviceGrain.DeviceState>();
        var grain = new DeviceGrain(state);
        var timestamp = new DateTimeOffset(2026, 2, 4, 11, 0, 0, TimeSpan.FromHours(-5));

        await grain.UpsertAsync(new TelemetryMsg
        {
            TenantId = "t1",
            DeviceId = "d1",
            Sequence = 3,
            Timestamp = timestamp,
            BuildingName = "b1",
            SpaceId = "s1",
            Properties = new Dictionary<string, object>
            {
                ["p1"] = 10,
                ["p2"] = "ok"
            }
        });

        var snapshot = await grain.GetAsync();
        snapshot.LastSequence.Should().Be(3);
        snapshot.UpdatedAt.Should().Be(timestamp.ToUniversalTime());
        snapshot.LatestProps.Should().ContainKey("p1").WhoseValue.Should().Be(10);
        snapshot.LatestProps.Should().ContainKey("p2").WhoseValue.Should().Be("ok");
    }

    [Fact]
    public async Task UpsertAsync_IgnoresStaleSequence()
    {
        var state = new TestPersistentState<DeviceGrain.DeviceState>();
        var grain = new DeviceGrain(state);

        await grain.UpsertAsync(new TelemetryMsg
        {
            TenantId = "t1",
            DeviceId = "d1",
            Sequence = 5,
            Timestamp = DateTimeOffset.UtcNow,
            Properties = new Dictionary<string, object>
            {
                ["p1"] = 1
            }
        });

        await grain.UpsertAsync(new TelemetryMsg
        {
            TenantId = "t1",
            DeviceId = "d1",
            Sequence = 4,
            Timestamp = DateTimeOffset.UtcNow,
            Properties = new Dictionary<string, object>
            {
                ["p1"] = 2
            }
        });

        var snapshot = await grain.GetAsync();
        snapshot.LastSequence.Should().Be(5);
        snapshot.LatestProps["p1"].Should().Be(1);
    }

    [Fact]
    public async Task State_PersistsAcrossInstances()
    {
        var state = new TestPersistentState<DeviceGrain.DeviceState>();
        var grain = new DeviceGrain(state);

        await grain.UpsertAsync(new TelemetryMsg
        {
            TenantId = "t1",
            DeviceId = "d1",
            Sequence = 2,
            Timestamp = DateTimeOffset.UtcNow,
            Properties = new Dictionary<string, object>
            {
                ["p1"] = 7.7
            }
        });

        var reactivated = new DeviceGrain(state);
        var snapshot = await reactivated.GetAsync();
        snapshot.LastSequence.Should().Be(2);
        snapshot.LatestProps.Should().ContainKey("p1").WhoseValue.Should().Be(7.7);
    }
}
