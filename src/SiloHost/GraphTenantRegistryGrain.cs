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

    public async Task RegisterTenantAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        var normalized = tenantId.Trim();
        if (_state.State.Tenants.Any(t => string.Equals(t, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _state.State.Tenants.Add(normalized);
        await _state.WriteStateAsync();
    }

    public Task<IReadOnlyList<string>> GetTenantIdsAsync()
    {
        return Task.FromResult<IReadOnlyList<string>>(_state.State.Tenants.ToArray());
    }

    [GenerateSerializer]
    public sealed class GraphTenantRegistryState
    {
        [Id(0)] public List<string> Tenants { get; set; } = new();
    }
}
