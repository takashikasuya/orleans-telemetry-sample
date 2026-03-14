using Microsoft.Extensions.DependencyInjection;

namespace Telemetry.Ingest;

public static class TelemetryEventSinkServiceCollectionExtensions
{
    public static IServiceCollection AddLoggingTelemetryEventSink(this IServiceCollection services)
    {
        services.AddSingleton<ITelemetryEventSink, LoggingTelemetryEventSink>();
        return services;
    }
}
