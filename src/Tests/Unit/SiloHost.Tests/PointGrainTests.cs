using System;
using FluentAssertions;
using Grains.Abstractions;
using Xunit;

namespace SiloHost.Tests;

public sealed class PointGrainTests
{
    [Fact]
    public async Task UpsertAsync_UpdatesStateForNewSequence()
    {
        var state = new TestPersistentState<PointGrain.PointState>();
        var grain = new PointGrain(state);
        var timestamp = new DateTimeOffset(2026, 2, 4, 10, 0, 0, TimeSpan.FromHours(9));

        await grain.UpsertAsync(new TelemetryPointMsg
        {
            TenantId = "t1",
            BuildingName = "b1",
            SpaceId = "s1",
            DeviceId = "d1",
            PointId = "p1",
            Sequence = 10,
            Timestamp = timestamp,
            Value = 42.5
        });

        var snapshot = await grain.GetAsync();
        snapshot.LastSequence.Should().Be(10);
        snapshot.LatestValue.Should().Be(42.5);
        snapshot.UpdatedAt.Should().Be(timestamp.ToUniversalTime());
    }

    [Fact]
    public async Task UpsertAsync_IgnoresStaleSequence()
    {
        var state = new TestPersistentState<PointGrain.PointState>();
        var grain = new PointGrain(state);

        await grain.UpsertAsync(new TelemetryPointMsg
        {
            TenantId = "t1",
            BuildingName = "b1",
            SpaceId = "s1",
            DeviceId = "d1",
            PointId = "p1",
            Sequence = 5,
            Timestamp = DateTimeOffset.UtcNow,
            Value = 1
        });

        await grain.UpsertAsync(new TelemetryPointMsg
        {
            TenantId = "t1",
            BuildingName = "b1",
            SpaceId = "s1",
            DeviceId = "d1",
            PointId = "p1",
            Sequence = 4,
            Timestamp = DateTimeOffset.UtcNow,
            Value = 2
        });

        var snapshot = await grain.GetAsync();
        snapshot.LastSequence.Should().Be(5);
        snapshot.LatestValue.Should().Be(1);
    }

    [Fact]
    public async Task State_PersistsAcrossInstances()
    {
        var state = new TestPersistentState<PointGrain.PointState>();
        var grain = new PointGrain(state);

        await grain.UpsertAsync(new TelemetryPointMsg
        {
            TenantId = "t1",
            BuildingName = "b1",
            SpaceId = "s1",
            DeviceId = "d1",
            PointId = "p1",
            Sequence = 7,
            Timestamp = DateTimeOffset.UtcNow,
            Value = 3.14
        });

        var reactivated = new PointGrain(state);
        var snapshot = await reactivated.GetAsync();
        snapshot.LastSequence.Should().Be(7);
        snapshot.LatestValue.Should().Be(3.14);
    }
}
