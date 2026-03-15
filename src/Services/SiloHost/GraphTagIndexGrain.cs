using Grains.Abstractions;
using Orleans;
using Orleans.Runtime;

namespace SiloHost;

public sealed class GraphTagIndexGrain : Grain, IGraphTagIndexGrain
{
    private readonly IPersistentState<GraphTagIndexState> _state;

    public GraphTagIndexGrain([PersistentState("graph-tag-index", "GraphTagIndexStore")] IPersistentState<GraphTagIndexState> state)
    {
        _state = state;
    }

    public async Task IndexNodeAsync(string nodeId, Dictionary<string, string> attributes)
    {
        var normalizedNodeId = nodeId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedNodeId))
        {
            return;
        }

        var nextTags = ExtractTags(attributes);
        _state.State.TagsByNode.TryGetValue(normalizedNodeId, out var previousTags);
        previousTags ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var removed = previousTags.Except(nextTags, StringComparer.OrdinalIgnoreCase).ToArray();
        var added = nextTags.Except(previousTags, StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var tag in removed)
        {
            if (_state.State.ByTag.TryGetValue(tag, out var nodeIds))
            {
                nodeIds.Remove(normalizedNodeId);
                if (nodeIds.Count == 0)
                {
                    _state.State.ByTag.Remove(tag);
                }
            }
        }

        foreach (var tag in added)
        {
            if (!_state.State.ByTag.TryGetValue(tag, out var nodeIds))
            {
                nodeIds = new HashSet<string>(StringComparer.Ordinal);
                _state.State.ByTag[tag] = nodeIds;
            }

            nodeIds.Add(normalizedNodeId);
        }

        if (nextTags.Count == 0)
        {
            _state.State.TagsByNode.Remove(normalizedNodeId);
        }
        else
        {
            _state.State.TagsByNode[normalizedNodeId] = nextTags;
        }

        await _state.WriteStateAsync();
    }

    public async Task RemoveNodeAsync(string nodeId)
    {
        var normalizedNodeId = nodeId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedNodeId))
        {
            return;
        }

        if (!_state.State.TagsByNode.Remove(normalizedNodeId, out var tags))
        {
            return;
        }

        foreach (var tag in tags)
        {
            if (_state.State.ByTag.TryGetValue(tag, out var nodeIds))
            {
                nodeIds.Remove(normalizedNodeId);
                if (nodeIds.Count == 0)
                {
                    _state.State.ByTag.Remove(tag);
                }
            }
        }

        await _state.WriteStateAsync();
    }

    public Task<IReadOnlyList<string>> GetNodeIdsByTagsAsync(IReadOnlyList<string> tags)
    {
        var normalizedTags = NormalizeTags(tags);
        if (normalizedTags.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        HashSet<string>? intersection = null;
        foreach (var tag in normalizedTags)
        {
            if (!_state.State.ByTag.TryGetValue(tag, out var nodeIds) || nodeIds.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }

            if (intersection is null)
            {
                intersection = new HashSet<string>(nodeIds, StringComparer.Ordinal);
                continue;
            }

            intersection.IntersectWith(nodeIds);
            if (intersection.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }
        }

        var results = intersection is null
            ? Array.Empty<string>()
            : intersection.OrderBy(x => x, StringComparer.Ordinal).ToArray();

        return Task.FromResult<IReadOnlyList<string>>(results);
    }

    private static HashSet<string> ExtractTags(IReadOnlyDictionary<string, string>? attributes)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (attributes is null)
        {
            return tags;
        }

        foreach (var pair in attributes)
        {
            if (!pair.Key.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsTruthy(pair.Value))
            {
                continue;
            }

            var tag = pair.Key[4..].Trim();
            if (!string.IsNullOrWhiteSpace(tag))
            {
                tags.Add(tag);
            }
        }

        return tags;
    }

    private static List<string> NormalizeTags(IReadOnlyList<string> tags)
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

        return normalized.ToList();
    }

    private static bool IsTruthy(string? value)
        => value is not null
           && (value.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase)
               || value.Equals("1", StringComparison.OrdinalIgnoreCase));

    [GenerateSerializer]
    public sealed class GraphTagIndexState
    {
        [Id(0)]
        public Dictionary<string, HashSet<string>> ByTag { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [Id(1)]
        public Dictionary<string, HashSet<string>> TagsByNode { get; set; } = new(StringComparer.Ordinal);
    }
}
