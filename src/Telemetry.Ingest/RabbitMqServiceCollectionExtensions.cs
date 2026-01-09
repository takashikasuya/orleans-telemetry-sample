using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telemetry.Ingest;

namespace Telemetry.Ingest.RabbitMq;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqIngest(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RabbitMqIngestOptions>(configuration);
        services.AddSingleton<ITelemetryIngestConnector, RabbitMqIngestConnector>();
        return services;
    }
}
