using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ApiGateway.Infrastructure;
using FluentAssertions;
using Grains.Abstractions;
using Moq;
using Xunit;

namespace ApiGateway.Tests;

public sealed class GraphTraversalTests
{
    [Fact]
    public async Task TraverseAsync_WithLimitedDepth_ReturnsOnlyNeighborsUpToDepth()
    {
        var snapshots = new Dictionary<string, GraphNodeSnapshot>
        {
            ["start"] = CreateSnapshot("start",
                new GraphEdge { Predicate = "isPartOf", TargetNodeId = "neighbor-a" },
                new GraphEdge { Predicate = "isPartOf", TargetNodeId = "neighbor-b" }),
            ["neighbor-a"] = CreateSnapshot("neighbor-a",
                new GraphEdge { Predicate = "isPartOf", TargetNodeId = "deep-node" }),
            ["neighbor-b"] = CreateSnapshot("neighbor-b"),
            ["deep-node"] = CreateSnapshot("deep-node")
        };

        var traversal = new GraphTraversal();
        var client = BuildClusterMock(snapshots);

        var result = await traversal.TraverseAsync(client.Object, "tenant-x", "start", depth: 1, predicate: null);

        result.StartNodeId.Should().Be("start");
        result.Depth.Should().Be(1);
        var visited = result.Nodes.Select(n => n.Node.NodeId).ToArray();
        visited.Should().BeEquivalentTo("start", "neighbor-a", "neighbor-b");
        visited.Should().NotContain("deep-node");
    }

    [Fact]
    public async Task TraverseAsync_WithPredicate_FiltersEdges()
    {
        var snapshots = new Dictionary<string, GraphNodeSnapshot>
        {
            ["root"] = CreateSnapshot("root",
                new GraphEdge { Predicate = "match", TargetNodeId = "match-target" },
                new GraphEdge { Predicate = "skip", TargetNodeId = "skip-target" }),
            ["match-target"] = CreateSnapshot("match-target",
                new GraphEdge { Predicate = "match", TargetNodeId = "match-grandchild" }),
            ["skip-target"] = CreateSnapshot("skip-target"),
            ["match-grandchild"] = CreateSnapshot("match-grandchild")
        };

        var traversal = new GraphTraversal();
        var client = BuildClusterMock(snapshots);

        var result = await traversal.TraverseAsync(client.Object, "tenant-x", "root", depth: 2, predicate: "match");

        var visited = result.Nodes.Select(n => n.Node.NodeId).ToArray();
        visited.Should().BeEquivalentTo("root", "match-target", "match-grandchild");
        visited.Should().NotContain("skip-target");
    }

    [Fact]
    public async Task TraverseAsync_WithZeroDepth_ReturnsOnlyStartNode()
    {
        var snapshots = new Dictionary<string, GraphNodeSnapshot>
        {
            ["self"] = CreateSnapshot("self",
                new GraphEdge { Predicate = "loop", TargetNodeId = "self" })
        };

        var traversal = new GraphTraversal();
        var client = BuildClusterMock(snapshots);

        var result = await traversal.TraverseAsync(client.Object, "tenant-x", "self", depth: 0, predicate: null);

        result.Nodes.Should().HaveCount(1);
        result.Nodes[0].Node.NodeId.Should().Be("self");
    }

    [Fact]
    public async Task TraverseAsync_WithCycles_DoesNotDuplicateNodes()
    {
        var snapshots = new Dictionary<string, GraphNodeSnapshot>
        {
            ["cycle-a"] = CreateSnapshot("cycle-a",
                new GraphEdge { Predicate = "loop", TargetNodeId = "cycle-b" }),
            ["cycle-b"] = CreateSnapshot("cycle-b",
                new GraphEdge { Predicate = "loop", TargetNodeId = "cycle-a" })
        };

        var traversal = new GraphTraversal();
        var client = BuildClusterMock(snapshots);

        var result = await traversal.TraverseAsync(client.Object, "tenant-x", "cycle-a", depth: 5, predicate: null);

        var visited = result.Nodes.Select(n => n.Node.NodeId).ToArray();
        visited.Should().Equal(new[] { "cycle-a", "cycle-b" });
    }

    [Fact]
    public async Task TraverseAsync_WithSufficientDepth_IncludesDeepNode()
    {
        var snapshots = new Dictionary<string, GraphNodeSnapshot>
        {
            ["root"] = CreateSnapshot("root",
                new GraphEdge { Predicate = "step", TargetNodeId = "level-1" }),
            ["level-1"] = CreateSnapshot("level-1",
                new GraphEdge { Predicate = "step", TargetNodeId = "level-2" }),
            ["level-2"] = CreateSnapshot("level-2",
                new GraphEdge { Predicate = "step", TargetNodeId = "level-3" }),
            ["level-3"] = CreateSnapshot("level-3")
        };

        var traversal = new GraphTraversal();
        var client = BuildClusterMock(snapshots);

        var result = await traversal.TraverseAsync(client.Object, "tenant-x", "root", depth: 3, predicate: null);

        result.Nodes.Select(n => n.Node.NodeId).Should().Contain("level-3");
    }

    private static GraphNodeSnapshot CreateSnapshot(string nodeId, params GraphEdge[] outgoingEdges)
    {
        return new GraphNodeSnapshot
        {
            Node = new GraphNodeDefinition
            {
                NodeId = nodeId,
                DisplayName = $"display-{nodeId}",
                NodeType = GraphNodeType.Unknown
            },
            OutgoingEdges = outgoingEdges.ToList()
        };
    }

    private static Mock<IClusterClient> BuildClusterMock(IReadOnlyDictionary<string, GraphNodeSnapshot> snapshots)
    {
        var nodeGrains = snapshots.ToDictionary(
            kvp => kvp.Key,
            kvp =>
            {
                var grainMock = new Mock<IGraphNodeGrain>();
                grainMock.Setup(g => g.GetAsync()).ReturnsAsync(kvp.Value);
                return grainMock.Object;
            });

        var clientMock = new Mock<IClusterClient>();
        clientMock
            .Setup(client => client.GetGrain<IGraphNodeGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((key, _) =>
            {
                var nodeId = ExtractNodeId(key);
                return nodeGrains.TryGetValue(nodeId, out var grain)
                    ? grain
                    : throw new KeyNotFoundException($"No grain registered for node '{nodeId}'.");
            });

        return clientMock;
    }

    private static string ExtractNodeId(string key)
    {
        var colonIndex = key.IndexOf(':');
        return colonIndex >= 0 ? key[(colonIndex + 1)..] : key;
    }
}
