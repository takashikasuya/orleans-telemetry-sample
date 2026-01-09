using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Telemetry.Ingest;

public static class TelemetryIngestServiceCollectionExtensions
{
    public static IServiceCollection AddTelemetryIngest(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TelemetryIngestOptions>(configuration);
        services.AddHostedService<TelemetryIngestCoordinator>();
        return services;
    }
}
