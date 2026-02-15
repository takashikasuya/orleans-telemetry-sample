using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grains.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Orleans;

namespace ApiGateway.Services;

public sealed record PointGatewayResolution(
    string? GatewayId,
    string? PointNodeId);

public sealed class PointGatewayResolver
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IClusterClient _client;
    private readonly IMemoryCache _cache;

    public PointGatewayResolver(IClusterClient client, IMemoryCache cache)
    {
        _client = client;
        _cache = cache;
    }

    public async Task<PointGatewayResolution> ResolveAsync(string tenantId, string deviceId, string pointId)
    {
        var cacheKey = $"{tenantId}:{deviceId}:{pointId}";
        if (_cache.TryGetValue<PointGatewayResolution>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var index = _client.GetGrain<IGraphIndexGrain>(tenantId);
        var equipmentIds = await index.GetByTypeAsync(GraphNodeType.Equipment);
        if (equipmentIds is null || equipmentIds.Count == 0)
        {
            return CacheAndReturn(cacheKey, new PointGatewayResolution(null, null));
        }

        foreach (var equipmentId in equipmentIds)
        {
            if (string.IsNullOrWhiteSpace(equipmentId))
            {
                continue;
            }

            var equipmentSnapshot = await LoadNodeAsync(tenantId, equipmentId);
            if (equipmentSnapshot?.Node?.Attributes is null)
            {
                continue;
            }

            if (!TryGetAttribute(equipmentSnapshot.Node.Attributes, "DeviceId", out var candidateDeviceId)
                || !string.Equals(candidateDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryGetAttribute(equipmentSnapshot.Node.Attributes, "GatewayId", out var gatewayId);

            var pointTargets = equipmentSnapshot.OutgoingEdges
                .Where(edge => string.Equals(edge.Predicate, "hasPoint", StringComparison.OrdinalIgnoreCase))
                .Select(edge => edge.TargetNodeId)
                .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var pointNodeId in pointTargets)
            {
                var pointSnapshot = await LoadNodeAsync(tenantId, pointNodeId);
                if (pointSnapshot?.Node?.Attributes is null)
                {
                    continue;
                }

                if (!TryGetAttribute(pointSnapshot.Node.Attributes, "PointId", out var candidatePointId)
                    || !string.Equals(candidatePointId, pointId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryGetAttribute(pointSnapshot.Node.Attributes, "GatewayId", out var pointGatewayId))
                {
                    gatewayId = pointGatewayId;
                }

                return CacheAndReturn(cacheKey, new PointGatewayResolution(gatewayId, pointSnapshot.Node.NodeId));
            }
        }

        return CacheAndReturn(cacheKey, new PointGatewayResolution(null, null));
    }

    private async Task<GraphNodeSnapshot?> LoadNodeAsync(string tenantId, string nodeId)
    {
        var grainKey = GraphNodeKey.Create(tenantId, nodeId);
        var grain = _client.GetGrain<IGraphNodeGrain>(grainKey);
        return await grain.GetAsync();
    }

    private PointGatewayResolution CacheAndReturn(string cacheKey, PointGatewayResolution resolution)
    {
        _cache.Set(cacheKey, resolution, CacheDuration);
        return resolution;
    }

    private static bool TryGetAttribute(
        IReadOnlyDictionary<string, string> attributes,
        string key,
        out string? value)
    {
        if (attributes.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            return true;
        }

        value = null;
        return false;
    }
}
