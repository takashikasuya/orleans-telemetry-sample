using System;
using System.Linq;
using Grains.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telemetry.Ingest;
using Telemetry.Ingest.Simulator;

namespace SiloHost;

internal sealed class GraphSeedService : BackgroundService
{
    private readonly GraphSeeder _seeder;
    private readonly TelemetryIngestOptions _ingestOptions;
    private readonly SimulatorIngestOptions _simulatorOptions;
    private readonly ILogger<GraphSeedService> _logger;

    public GraphSeedService(
        GraphSeeder seeder,
        IOptions<TelemetryIngestOptions> ingestOptions,
        IOptions<SimulatorIngestOptions> simulatorOptions,
        ILogger<GraphSeedService> logger)
    {
        _seeder = seeder;
        _ingestOptions = ingestOptions.Value;
        _simulatorOptions = simulatorOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seedPath = Environment.GetEnvironmentVariable("RDF_SEED_PATH");
        var tenantEnv = Environment.GetEnvironmentVariable("TENANT_ID");
        var tenant = string.IsNullOrWhiteSpace(tenantEnv) ? "default" : tenantEnv;
        var tenantNameEnv = Environment.GetEnvironmentVariable("TENANT_NAME");
        var tenantName = string.IsNullOrWhiteSpace(tenantNameEnv) ? tenant : tenantNameEnv.Trim();
        var seededAny = false;

        if (!string.IsNullOrWhiteSpace(seedPath))
        {
            _logger.LogInformation("Seeding graph from RDF: {Path} (tenant: {Tenant})", seedPath, tenant);
            var result = await _seeder.SeedAsync(seedPath, tenant, tenantName, stoppingToken);
            if (!result.Success)
            {
                _logger.LogError("Graph seeding reported failure: {Message}", result.Message);
                return;
            }

            _logger.LogInformation("Graph seed completed. Nodes={NodeCount}, Edges={EdgeCount}", result.NodeCount, result.EdgeCount);
            seededAny = true;
        }

        if (IsSimulatorEnabled())
        {
            var simulatorTenant = ResolveSimulatorTenant(tenantEnv);
            var simulatorTenantName = ResolveSimulatorTenantName(simulatorTenant, tenantNameEnv);
            _logger.LogInformation("Seeding graph from Simulator settings (tenant: {Tenant})", simulatorTenant);
            var turtle = SimulatorGraphSeedBuilder.BuildTurtle(_simulatorOptions);
            var result = await _seeder.SeedFromContentAsync(turtle, "simulator-generated", simulatorTenant, simulatorTenantName, stoppingToken);
            if (!result.Success)
            {
                _logger.LogError("Simulator graph seeding reported failure: {Message}", result.Message);
                return;
            }

            _logger.LogInformation("Simulator graph seed completed. Nodes={NodeCount}, Edges={EdgeCount}", result.NodeCount, result.EdgeCount);
            seededAny = true;
        }

        if (!seededAny)
        {
            _logger.LogInformation("No RDF seed path or Simulator configuration detected. Skipping graph seed.");
        }
    }

    private bool IsSimulatorEnabled()
    {
        var enabled = _ingestOptions.Enabled;
        if (enabled is null || enabled.Length == 0)
        {
            return true;
        }

        return enabled.Any(name => string.Equals(name, "Simulator", StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveSimulatorTenant(string? envTenant)
    {
        if (!string.IsNullOrWhiteSpace(envTenant))
        {
            return envTenant;
        }

        return string.IsNullOrWhiteSpace(_simulatorOptions.TenantId) ? "default" : _simulatorOptions.TenantId;
    }

    private static string ResolveSimulatorTenantName(string simulatorTenantId, string? envTenantName)
    {
        if (!string.IsNullOrWhiteSpace(envTenantName))
        {
            return envTenantName.Trim();
        }

        return simulatorTenantId;
    }
}
