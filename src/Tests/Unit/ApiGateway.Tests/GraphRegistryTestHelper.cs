using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ApiGateway.Services;
using Grains.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ApiGateway.Tests;

internal static class GraphRegistryTestHelper
{
    public static GraphRegistryService CreateGraphRegistry(
        IReadOnlyDictionary<GraphNodeType, IReadOnlyList<string>> nodesByType,
        int maxInlineRecords,
        out RegistryExportService exportService,
        out string tempRoot)
    {
        tempRoot = CreateTempDirectory();
        var options = Options.Create(new RegistryExportOptions
        {
            ExportRoot = tempRoot,
            MaxInlineRecords = maxInlineRecords,
            DefaultTtlMinutes = 5
        });

        exportService = new RegistryExportService(options, NullLogger<RegistryExportService>.Instance);
        var clusterMock = BuildClusterMock(nodesByType);
        return new GraphRegistryService(clusterMock.Object, exportService, options, NullLogger<GraphRegistryService>.Instance);
    }

    public static Mock<IClusterClient> BuildClusterMock(IReadOnlyDictionary<GraphNodeType, IReadOnlyList<string>> nodesByType)
    {
        var indexMock = new Mock<IGraphIndexGrain>();
        indexMock
            .Setup(x => x.GetByTypeAsync(It.IsAny<GraphNodeType>()))
            .ReturnsAsync((GraphNodeType type) =>
            {
                return nodesByType.TryGetValue(type, out var ids)
                    ? ids
                    : Array.Empty<string>();
            });

        var totals = nodesByType.SelectMany(kvp => kvp.Value).Distinct().ToList();
        var snapshots = totals.ToDictionary(
            nodeId => nodeId,
            nodeId => new GraphNodeSnapshot
            {
                Node = new GraphNodeDefinition
                {
                    NodeId = nodeId,
                    DisplayName = $"{nodeId}-display",
                    NodeType = DetermineNodeType(nodeId, nodesByType)
                }
            });

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
            .Setup(c => c.GetGrain<IGraphIndexGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(indexMock.Object);

        clientMock
            .Setup(c => c.GetGrain<IGraphNodeGrain>(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>((key, _) =>
            {
                var nodeId = ExtractNodeId(key);
                return nodeGrains.TryGetValue(nodeId, out var grain)
                    ? grain
                    : throw new KeyNotFoundException($"No grain registered for node '{nodeId}'.");
            });

        return clientMock;
    }

    public static void CleanupTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "graph-registry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ExtractNodeId(string key)
    {
        var colon = key.IndexOf(':');
        return colon >= 0 ? key[(colon + 1)..] : key;
    }

    private static GraphNodeType DetermineNodeType(string nodeId, IReadOnlyDictionary<GraphNodeType, IReadOnlyList<string>> nodesByType)
    {
        foreach (var kvp in nodesByType)
        {
            if (kvp.Value.Contains(nodeId))
            {
                return kvp.Key;
            }
        }

        return GraphNodeType.Unknown;
    }
}
