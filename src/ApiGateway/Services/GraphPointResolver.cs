using ApiGateway.Models;
using Grains.Abstractions;
using Orleans;

namespace ApiGateway.Services;

public sealed class GraphPointResolver
{
    private readonly IClusterClient _client;

    public GraphPointResolver(IClusterClient client)
    {
        _client = client;
    }

    public async Task<Dictionary<string, PointPropertyValue>> GetPointsForNodeAsync(
        string tenantId,
        GraphNodeSnapshot snapshot)
    {
        var results = new Dictionary<string, PointPropertyValue>(StringComparer.OrdinalIgnoreCase);
        if (snapshot.OutgoingEdges is null || snapshot.OutgoingEdges.Count == 0)
        {
            return results;
        }

        var pointTargets = snapshot.OutgoingEdges
            .Where(edge => string.Equals(edge.Predicate, "hasPoint", StringComparison.OrdinalIgnoreCase))
            .Select(edge => edge.TargetNodeId)
            .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var pointNodeId in pointTargets)
        {
            var pointSnapshot = await LoadNodeAsync(tenantId, pointNodeId);
            if (pointSnapshot is null)
            {
                continue;
            }

            await AddPointAsync(results, tenantId, pointSnapshot, expectedDeviceId: null);
        }

        return results;
    }

    public async Task<Dictionary<string, PointPropertyValue>> GetPointsForDeviceAsync(
        string tenantId,
        string deviceId)
    {
        var results = new Dictionary<string, PointPropertyValue>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return results;
        }

        var index = _client.GetGrain<IGraphIndexGrain>(tenantId);
        var equipmentIds = await index.GetByTypeAsync(GraphNodeType.Equipment);
        if (equipmentIds is null || equipmentIds.Count == 0)
        {
            return results;
        }

        foreach (var equipmentId in equipmentIds)
        {
            if (string.IsNullOrWhiteSpace(equipmentId))
            {
                continue;
            }

            var equipmentSnapshot = await LoadNodeAsync(tenantId, equipmentId);
            if (equipmentSnapshot is null)
            {
                continue;
            }

            if (!TryGetAttribute(equipmentSnapshot.Node.Attributes, "DeviceId", out var equipmentDeviceId))
            {
                continue;
            }

            if (!string.Equals(deviceId, equipmentDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var pointTargets = equipmentSnapshot.OutgoingEdges
                .Where(edge => string.Equals(edge.Predicate, "hasPoint", StringComparison.OrdinalIgnoreCase))
                .Select(edge => edge.TargetNodeId)
                .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var pointNodeId in pointTargets)
            {
                var pointSnapshot = await LoadNodeAsync(tenantId, pointNodeId);
                if (pointSnapshot is null)
                {
                    continue;
                }

                await AddPointAsync(results, tenantId, pointSnapshot, expectedDeviceId: deviceId);
            }
        }

        return results;
    }

    private async Task<GraphNodeSnapshot?> LoadNodeAsync(string tenantId, string nodeId)
    {
        var grainKey = GraphNodeKey.Create(tenantId, nodeId);
        var grain = _client.GetGrain<IGraphNodeGrain>(grainKey);
        return await grain.GetAsync();
    }

    private async Task AddPointAsync(
        Dictionary<string, PointPropertyValue> results,
        string tenantId,
        GraphNodeSnapshot pointSnapshot,
        string? expectedDeviceId)
    {
        if (pointSnapshot.Node?.Attributes is null)
        {
            return;
        }

        var attributes = pointSnapshot.Node.Attributes;

        if (!TryGetAttribute(attributes, "PointId", out var pointId))
        {
            return;
        }

        TryGetAttribute(attributes, "DeviceId", out var deviceId);
        if (!string.IsNullOrWhiteSpace(expectedDeviceId) &&
            (string.IsNullOrWhiteSpace(deviceId) ||
             !string.Equals(deviceId, expectedDeviceId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        TryGetAttribute(attributes, "PointType", out var pointType);

        var pointKey = PointGrainKey.Create(tenantId, pointId);
        var pointGrain = _client.GetGrain<IPointGrain>(pointKey);
        var snapshot = await pointGrain.GetAsync();

        var keyBase = string.IsNullOrWhiteSpace(pointType) ? pointId : pointType;
        if (string.IsNullOrWhiteSpace(keyBase))
        {
            keyBase = pointSnapshot.Node.NodeId;
        }

        var key = EnsureUniqueKey(results, keyBase);
        results[key] = new PointPropertyValue(snapshot.LatestValue, snapshot.UpdatedAt);
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

    private static string EnsureUniqueKey(
        IDictionary<string, PointPropertyValue> results,
        string keyBase)
    {
        if (!results.ContainsKey(keyBase))
        {
            return keyBase;
        }

        var index = 2;
        while (results.ContainsKey($"{keyBase}_{index}"))
        {
            index++;
        }

        return $"{keyBase}_{index}";
    }
}
