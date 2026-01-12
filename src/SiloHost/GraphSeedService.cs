using Grains.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SiloHost;

internal sealed class GraphSeedService : BackgroundService
{
    private readonly GraphSeeder _seeder;
    private readonly ILogger<GraphSeedService> _logger;

    public GraphSeedService(
        GraphSeeder seeder,
        ILogger<GraphSeedService> logger)
    {
        _seeder = seeder;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seedPath = Environment.GetEnvironmentVariable("RDF_SEED_PATH");
        if (string.IsNullOrWhiteSpace(seedPath))
        {
            _logger.LogInformation("RDF_SEED_PATH is not set. Skipping graph seed.");
            return;
        }

        var tenant = Environment.GetEnvironmentVariable("TENANT_ID") ?? "default";
        _logger.LogInformation("Seeding graph from RDF: {Path} (tenant: {Tenant})", seedPath, tenant);

        var result = await _seeder.SeedAsync(seedPath, tenant, stoppingToken);
        if (!result.Success)
        {
            _logger.LogError("Graph seeding reported failure: {Message}", result.Message);
            return;
        }

        _logger.LogInformation("Graph seed completed. Nodes={NodeCount}, Edges={EdgeCount}", result.NodeCount, result.EdgeCount);
    }
}
