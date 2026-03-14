using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telemetry.Ingest;

namespace Telemetry.Ingest.Simulator;

public static class SimulatorServiceCollectionExtensions
{
    public static IServiceCollection AddSimulatorIngest(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SimulatorIngestOptions>(configuration);
        services.AddSingleton<ITelemetryIngestConnector, SimulatorIngestConnector>();
        return services;
    }
}
