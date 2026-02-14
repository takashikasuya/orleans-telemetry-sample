using System;
using System.Collections.Generic;
using System.Linq;
using Grains.Abstractions;
using Orleans;
using Orleans.Runtime;

namespace SiloHost;

internal sealed class GraphTenantRegistryGrain : Grain, IGraphTenantRegistryGrain
{
    private readonly IPersistentState<GraphTenantRegistryState> _state;

    public GraphTenantRegistryGrain([PersistentState("graph-tenant-registry", "GraphTenantStore")] IPersistentState<GraphTenantRegistryState> state)
    {
        _state = state;
    }

    public async Task RegisterTenantAsync(string tenantId, string? tenantName = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        var normalizedId = tenantId.Trim();
        var normalizedName = NormalizeTenantName(tenantName, normalizedId);

        var existing = _state.State.Tenants
            .FirstOrDefault(t => string.Equals(t.TenantId, normalizedId, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            if (string.IsNullOrWhiteSpace(existing.TenantName) ||
                !string.Equals(existing.TenantName, normalizedName, StringComparison.Ordinal))
            {
                existing.TenantName = normalizedName;
                await _state.WriteStateAsync();
            }

            return;
        }

        _state.State.Tenants.Add(new GraphTenantInfo
        {
            TenantId = normalizedId,
            TenantName = normalizedName
        });
        await _state.WriteStateAsync();
    }

    public Task<IReadOnlyList<string>> GetTenantIdsAsync()
    {
        var ids = _state.State.Tenants
            .Select(t => t.TenantId)
            .ToArray();
        return Task.FromResult<IReadOnlyList<string>>(ids);
    }

    public Task<IReadOnlyList<GraphTenantInfo>> GetTenantsAsync()
    {
        var tenants = _state.State.Tenants
            .Select(t => new GraphTenantInfo
            {
                TenantId = t.TenantId,
                TenantName = string.IsNullOrWhiteSpace(t.TenantName) ? t.TenantId : t.TenantName
            })
            .ToArray();
        return Task.FromResult<IReadOnlyList<GraphTenantInfo>>(tenants);
    }

    [GenerateSerializer]
    public sealed class GraphTenantRegistryState
    {
        [Id(0)] public List<GraphTenantInfo> Tenants { get; set; } = new();
    }

    private static string NormalizeTenantName(string? tenantName, string tenantId)
        => string.IsNullOrWhiteSpace(tenantName) ? tenantId : tenantName.Trim();
}
