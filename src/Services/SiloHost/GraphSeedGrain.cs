using System.Threading;
using System.Threading.Tasks;
using Grains.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;

namespace SiloHost;

internal sealed class GraphSeedGrain : Grain, IGraphSeedGrain
{
    private readonly GraphSeeder _seeder;
    private readonly ILogger<GraphSeedGrain> _logger;
    private GraphSeedStatus? _lastStatus;

    public GraphSeedGrain(GraphSeeder seeder, ILogger<GraphSeedGrain> logger)
    {
        _seeder = seeder;
        _logger = logger;
    }

    public Task<GraphSeedStatus?> GetLastResultAsync()
    {
        return Task.FromResult(_lastStatus);
    }

    public async Task<GraphSeedStatus> SeedAsync(GraphSeedRequest request)
    {
        var status = await _seeder.SeedAsync(request.RdfPath, request.TenantId, request.TenantName, CancellationToken.None);
        _lastStatus = status;
        if (!status.Success)
        {
            _logger.LogWarning("Manual graph seed failed: {Message}", status.Message);
        }

        return status;
    }
}
