using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telemetry.Ingest;

namespace Telemetry.Ingest.Kafka;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKafkaIngest(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<KafkaIngestOptions>(configuration);
        services.AddSingleton<ITelemetryIngestConnector, KafkaIngestConnector>();
        return services;
    }
}
