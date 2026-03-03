using System;
using System.Collections.Generic;
using System.Linq;
using Grains.Abstractions;
using Orleans;

namespace ApiGateway.Services;

/// <summary>
/// Provides tenant-scoped tag search against graph nodes and derived grains.
/// </summary>
public sealed class TagSearchService
{
    private readonly IClusterClient _client;

    public TagSearchService(IClusterClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Searches graph nodes matching all provided tags.
    /// </summary>
    /// <param name="tenant">Tenant identifier.</param>
    /// <param name="tags">Input tags.</param>
    /// <param name="limit">Optional item limit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Matched node search result.</returns>
    public async Task<TagNodeSearchResult> SearchNodesByTagsAsync(
        string tenant,
        IReadOnlyCollection<string> tags,
        int? limit,
        CancellationToken ct)
    {
        var normalizedTags = NormalizeTags(tags);
        if (normalizedTags.Count == 0 || (limit.HasValue && limit.Value <= 0))
        {
            return TagNodeSearchResult.Empty;
        }

        var nodes = await FindMatchingNodesAsync(tenant, normalizedTags, ct);
        var selected = ApplyLimit(nodes, limit);
        return new TagNodeSearchResult(selected);
    }

    /// <summary>
    /// Searches grain identities derived from nodes matching all provided tags.
    /// </summary>
    /// <param name="tenant">Tenant identifier.</param>
    /// <param name="tags">Input tags.</param>
    /// <param name="limit">Optional item limit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Matched grain search result.</returns>
    public async Task<TagGrainSearchResult> SearchGrainsByTagsAsync(
        string tenant,
        IReadOnlyCollection<string> tags,
        int? limit,
        CancellationToken ct)
    {
        var normalizedTags = NormalizeTags(tags);
        if (normalizedTags.Count == 0 || (limit.HasValue && limit.Value <= 0))
        {
            return TagGrainSearchResult.Empty;
        }

        var nodes = await FindMatchingNodesAsync(tenant, normalizedTags, ct);
        var grains = new List<TagMatchedGrain>();
        foreach (var node in nodes)
        {
            if (TryCreateDeviceGrain(node, tenant, out var device))
            {
                grains.Add(device);
            }

            if (TryCreatePointGrain(node, tenant, out var point))
            {
                grains.Add(point);
            }
        }

        var selected = ApplyLimit(grains, limit)
            .OrderBy(x => x.SourceNodeId, StringComparer.Ordinal)
            .ThenBy(x => x.GrainType, StringComparer.Ordinal)
            .ToList();

        return new TagGrainSearchResult(selected);
    }

    private async Task<List<TagMatchedNode>> FindMatchingNodesAsync(
        string tenant,
        HashSet<string> normalizedTags,
        CancellationToken ct)
    {
        var tagIndex = _client.GetGrain<IGraphTagIndexGrain>(tenant);
        var nodeIds = await tagIndex.GetNodeIdsByTagsAsync(normalizedTags.ToArray());

        var results = new List<TagMatchedNode>(nodeIds.Count);
        foreach (var nodeId in nodeIds)
        {
            ct.ThrowIfCancellationRequested();
            var grainKey = GraphNodeKey.Create(tenant, nodeId);
            var nodeGrain = _client.GetGrain<IGraphNodeGrain>(grainKey);
            var snapshot = await nodeGrain.GetAsync();
            var node = snapshot.Node ?? new GraphNodeDefinition { NodeId = nodeId, NodeType = GraphNodeType.Unknown };
            var attrs = node.Attributes ?? new Dictionary<string, string>();

            if (!ContainsAllTags(attrs, normalizedTags, out var matchedTags))
            {
                continue;
            }

            results.Add(new TagMatchedNode(
                node.NodeId,
                node.NodeType,
                node.DisplayName,
                new Dictionary<string, string>(attrs),
                matchedTags));
        }

        return results
            .OrderBy(x => x.NodeId, StringComparer.Ordinal)
            .ToList();
    }

    private static bool ContainsAllTags(
        IReadOnlyDictionary<string, string> attributes,
        HashSet<string> requiredTags,
        out IReadOnlyList<string> matchedTags)
    {
        var availableTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in attributes)
        {
            if (!kv.Key.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsTruthy(kv.Value))
            {
                continue;
            }

            var key = kv.Key[4..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                availableTags.Add(key);
            }
        }

        if (!requiredTags.All(tag => availableTags.Contains(tag)))
        {
            matchedTags = Array.Empty<string>();
            return false;
        }

        matchedTags = requiredTags
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return true;
    }

    private static bool TryCreateDeviceGrain(TagMatchedNode node, string tenant, out TagMatchedGrain grain)
    {
        grain = default!;
        if (node.NodeType is not (GraphNodeType.Equipment or GraphNodeType.Device))
        {
            return false;
        }

        if (!node.Attributes.TryGetValue("DeviceId", out var deviceId) || string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        var grainKey = DeviceGrainKey.Create(tenant, deviceId);
        grain = new TagMatchedGrain(node.NodeId, node.NodeType, "Device", grainKey, node.MatchedTags);
        return true;
    }

    private static bool TryCreatePointGrain(TagMatchedNode node, string tenant, out TagMatchedGrain grain)
    {
        grain = default!;
        if (node.NodeType != GraphNodeType.Point)
        {
            return false;
        }

        if (!node.Attributes.TryGetValue("PointId", out var pointId) || string.IsNullOrWhiteSpace(pointId))
        {
            return false;
        }

        var grainKey = PointGrainKey.Create(tenant, pointId);

        grain = new TagMatchedGrain(node.NodeId, node.NodeType, "Point", grainKey, node.MatchedTags);
        return true;
    }

    private static List<T> ApplyLimit<T>(IReadOnlyList<T> source, int? limit)
    {
        if (!limit.HasValue)
        {
            return source.ToList();
        }

        var capped = Math.Min(limit.Value, source.Count);
        return source.Take(capped).ToList();
    }

    internal static HashSet<string> NormalizeTags(IReadOnlyCollection<string> tags)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(token))
                {
                    normalized.Add(token.Trim());
                }
            }
        }

        return normalized;
    }

    private static bool IsTruthy(string? value)
        => value is not null
           && (value.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase)
               || value.Equals("1", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Represents a node matched by tag search.
/// </summary>
/// <param name="NodeId">Node identifier.</param>
/// <param name="NodeType">Node type.</param>
/// <param name="DisplayName">Display name.</param>
/// <param name="Attributes">Node attributes.</param>
/// <param name="MatchedTags">Tags that matched the node.</param>
public sealed record TagMatchedNode(
    string NodeId,
    GraphNodeType NodeType,
    string DisplayName,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<string> MatchedTags);

/// <summary>
/// Represents node search results matched by tags.
/// </summary>
/// <param name="Items">Matched node items.</param>
public sealed record TagNodeSearchResult(IReadOnlyList<TagMatchedNode> Items)
{
    /// <summary>
    /// Gets matched item count.
    /// </summary>
    public int Count => Items.Count;

    /// <summary>
    /// Gets an empty result instance.
    /// </summary>
    public static TagNodeSearchResult Empty { get; } = new(Array.Empty<TagMatchedNode>());
}

/// <summary>
/// Represents a grain matched by tag search.
/// </summary>
/// <param name="SourceNodeId">Source node identifier.</param>
/// <param name="NodeType">Source node type.</param>
/// <param name="GrainType">Resolved grain type name.</param>
/// <param name="GrainKey">Resolved grain key.</param>
/// <param name="MatchedTags">Tags that matched the source node.</param>
public sealed record TagMatchedGrain(
    string SourceNodeId,
    GraphNodeType NodeType,
    string GrainType,
    string GrainKey,
    IReadOnlyList<string> MatchedTags);

/// <summary>
/// Represents grain search results matched by tags.
/// </summary>
/// <param name="Items">Matched grain items.</param>
public sealed record TagGrainSearchResult(IReadOnlyList<TagMatchedGrain> Items)
{
    /// <summary>
    /// Gets matched item count.
    /// </summary>
    public int Count => Items.Count;

    /// <summary>
    /// Gets an empty result instance.
    /// </summary>
    public static TagGrainSearchResult Empty { get; } = new(Array.Empty<TagMatchedGrain>());
}
