using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telemetry.Ingest;

namespace Telemetry.Ingest.Mqtt;

public static class MqttServiceCollectionExtensions
{
    public static IServiceCollection AddMqttIngest(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MqttIngestOptions>(configuration);
        services.AddSingleton<ITelemetryIngestConnector, MqttIngestConnector>();
        return services;
    }
}
