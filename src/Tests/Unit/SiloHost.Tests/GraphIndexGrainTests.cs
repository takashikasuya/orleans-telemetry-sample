using FluentAssertions;
using Grains.Abstractions;
using Xunit;

namespace SiloHost.Tests;

public sealed class GraphIndexGrainTests
{
    [Fact]
    public async Task AddNodeAsync_PopulatesByType()
    {
        var persistence = new TestPersistentState<GraphIndexGrain.GraphIndexState>(() => new GraphIndexGrain.GraphIndexState());
        var grain = new GraphIndexGrain(persistence);

        await grain.AddNodeAsync(new GraphNodeDefinition { NodeId = "node-site", NodeType = GraphNodeType.Area });
        await grain.AddNodeAsync(new GraphNodeDefinition { NodeId = "node-area", NodeType = GraphNodeType.Area });
        await grain.AddNodeAsync(new GraphNodeDefinition { NodeId = "node-point", NodeType = GraphNodeType.Point });

        var areaNodes = await grain.GetByTypeAsync(GraphNodeType.Area);
        var pointNodes = await grain.GetByTypeAsync(GraphNodeType.Point);

        areaNodes.Should().BeEquivalentTo(new[] { "node-site", "node-area" }, options => options.WithoutStrictOrdering());
        pointNodes.Should().BeEquivalentTo(new[] { "node-point" });
    }

    [Fact]
    public async Task RemoveNodeAsync_DropsNodeFromType()
    {
        var persistence = new TestPersistentState<GraphIndexGrain.GraphIndexState>(() => new GraphIndexGrain.GraphIndexState());
        var grain = new GraphIndexGrain(persistence);

        await grain.AddNodeAsync(new GraphNodeDefinition { NodeId = "node-area", NodeType = GraphNodeType.Area });
        await grain.RemoveNodeAsync("node-area", GraphNodeType.Area);

        var areaNodes = await grain.GetByTypeAsync(GraphNodeType.Area);
        areaNodes.Should().BeEmpty();
    }
}
