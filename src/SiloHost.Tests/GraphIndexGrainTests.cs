using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Grains.Abstractions;
using Orleans.Runtime;
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

internal sealed class TestPersistentState<TState> : IPersistentState<TState>
    where TState : class, new()
{
    private readonly Func<TState> _stateFactory;

    public TestPersistentState(Func<TState>? stateFactory = null)
    {
        _stateFactory = stateFactory ?? (() => new TState());
        State = _stateFactory();
        Configuration = new TestPersistentStateConfiguration();
    }

    public TState State { get; set; }

    public string Etag { get; set; } = string.Empty;

    public bool RecordExists { get; set; }

    public Task ClearStateAsync(CancellationToken cancellationToken = default)
    {
        State = _stateFactory();
        RecordExists = false;
        return Task.CompletedTask;
    }

    public Task ClearStateAsync()
    {
        return ClearStateAsync(CancellationToken.None);
    }

    public Task ReadStateAsync(CancellationToken cancellationToken = default)
    {
        RecordExists = true;
        return Task.CompletedTask;
    }

    public Task ReadStateAsync()
    {
        return ReadStateAsync(CancellationToken.None);
    }

    public Task WriteStateAsync(CancellationToken cancellationToken = default)
    {
        RecordExists = true;
        return Task.CompletedTask;
    }

    public Task WriteStateAsync()
    {
        return WriteStateAsync(CancellationToken.None);
    }

    public IPersistentStateConfiguration Configuration { get; }

    private sealed class TestPersistentStateConfiguration : IPersistentStateConfiguration
    {
        public string StateName { get; set; } = "graph-index";
        public string StorageName { get; set; } = "GraphIndexStore";
    }
}
