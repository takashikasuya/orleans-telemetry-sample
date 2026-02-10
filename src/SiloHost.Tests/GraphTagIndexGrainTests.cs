using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace SiloHost.Tests;

public sealed class GraphTagIndexGrainTests
{
    [Fact]
    public async Task IndexNodeAsync_StoresAndIntersectsTags()
    {
        var persistence = new TestPersistentState<GraphTagIndexGrain.GraphTagIndexState>(() => new GraphTagIndexGrain.GraphTagIndexState());
        var grain = new GraphTagIndexGrain(persistence);

        await grain.IndexNodeAsync("node-a", new Dictionary<string, string> { ["tag:hot"] = "true", ["tag:zone-a"] = "1" });
        await grain.IndexNodeAsync("node-b", new Dictionary<string, string> { ["tag:hot"] = "TRUE", ["tag:zone-b"] = "true" });

        var hot = await grain.GetNodeIdsByTagsAsync(new[] { "hot" });
        var hotZoneA = await grain.GetNodeIdsByTagsAsync(new[] { "hot", "zone-a" });

        hot.Should().BeEquivalentTo(new[] { "node-a", "node-b" }, options => options.WithoutStrictOrdering());
        hotZoneA.Should().BeEquivalentTo(new[] { "node-a" });
    }

    [Fact]
    public async Task IndexNodeAsync_ReindexRemovesStaleTags()
    {
        var persistence = new TestPersistentState<GraphTagIndexGrain.GraphTagIndexState>(() => new GraphTagIndexGrain.GraphTagIndexState());
        var grain = new GraphTagIndexGrain(persistence);

        await grain.IndexNodeAsync("node-a", new Dictionary<string, string> { ["tag:hot"] = "true" });
        await grain.IndexNodeAsync("node-a", new Dictionary<string, string> { ["tag:cold"] = "true" });

        var hot = await grain.GetNodeIdsByTagsAsync(new[] { "hot" });
        var cold = await grain.GetNodeIdsByTagsAsync(new[] { "cold" });

        hot.Should().BeEmpty();
        cold.Should().BeEquivalentTo(new[] { "node-a" });
    }

    [Fact]
    public async Task RemoveNodeAsync_RemovesFromAllTags()
    {
        var persistence = new TestPersistentState<GraphTagIndexGrain.GraphTagIndexState>(() => new GraphTagIndexGrain.GraphTagIndexState());
        var grain = new GraphTagIndexGrain(persistence);

        await grain.IndexNodeAsync("node-a", new Dictionary<string, string> { ["tag:hot"] = "true", ["tag:zone-a"] = "true" });
        await grain.RemoveNodeAsync("node-a");

        var byHot = await grain.GetNodeIdsByTagsAsync(new[] { "hot" });
        var byZone = await grain.GetNodeIdsByTagsAsync(new[] { "zone-a" });

        byHot.Should().BeEmpty();
        byZone.Should().BeEmpty();
    }
}
