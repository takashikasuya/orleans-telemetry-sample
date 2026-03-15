using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telemetry.Ingest;

namespace Telemetry.Storage;

public static class TelemetryStorageServiceCollectionExtensions
{
    public static IServiceCollection AddTelemetryStorage(
        this IServiceCollection services,
        Action<TelemetryStorageOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<TelemetryStorageOptions>();
        }

        services.AddSingleton<ParquetTelemetryEventSink>();
        services.AddSingleton<ITelemetryStorageQuery, ParquetTelemetryStorageQuery>();
        services.AddSingleton<TelemetryStorageCompactor>();
        services.AddHostedService<TelemetryStorageBackgroundService>();
        services.AddSingleton<ITelemetryEventSink>(sp => sp.GetRequiredService<ParquetTelemetryEventSink>());
        return services;
    }
}
