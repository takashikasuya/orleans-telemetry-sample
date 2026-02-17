using System.Collections.Generic;
using DataModel.Analyzer.Extensions;
using Grains.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;
using Telemetry.Ingest;
using Telemetry.Ingest.Kafka;
using Telemetry.Ingest.RabbitMq;
using Telemetry.Ingest.Simulator;
using Telemetry.Storage;
using SiloHost;

namespace AdminGateway.E2E.Tests;

internal static class AdminGatewayTestClusterSettings
{
    public static IReadOnlyDictionary<string, string?> SiloOverrides { get; set; } = new Dictionary<string, string?>();
}

internal sealed class AdminGatewayTestClusterConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(AdminGatewayTestClusterSettings.SiloOverrides)
            .Build();

        siloBuilder.ConfigureServices(services =>
        {
            services.AddDataModelAnalyzer();
            services.AddSerializer(serializer =>
            {
                serializer.AddAssembly(typeof(TelemetryRouterGrain).Assembly);
                serializer.AddAssembly(typeof(ITelemetryRouterGrain).Assembly);
                serializer.AddAssembly(typeof(IGraphNodeGrain).Assembly);
            });
            services.AddSingleton<ITelemetryRouterGrain>(provider =>
            {
                var grainFactory = provider.GetRequiredService<IGrainFactory>();
                return grainFactory.GetGrain<ITelemetryRouterGrain>(System.Guid.Empty);
            });

            var ingestSection = config.GetSection("TelemetryIngest");
            var storageSection = config.GetSection("TelemetryStorage");
            services.AddTelemetryIngest(ingestSection);
            services.AddKafkaIngest(ingestSection.GetSection("Kafka"));
            services.AddRabbitMqIngest(ingestSection.GetSection("RabbitMq"));
            services.AddSimulatorIngest(ingestSection.GetSection("Simulator"));
            services.AddLoggingTelemetryEventSink();
            services.Configure<TelemetryStorageOptions>(storageSection);
            services.AddTelemetryStorage();
            services.AddHostedService<TestGraphSeedService>();
        });

        siloBuilder.Configure<Orleans.Configuration.ClusterOptions>(options =>
        {
            options.ClusterId = "telemetry-cluster";
            options.ServiceId = "telemetry-service";
        });
        siloBuilder.AddMemoryGrainStorage("DeviceStore");
        siloBuilder.AddMemoryGrainStorage("GraphStore");
        siloBuilder.AddMemoryGrainStorage("GraphIndexStore");
        siloBuilder.AddMemoryGrainStorage("GraphTenantStore");
        siloBuilder.AddMemoryStreams("DeviceUpdates");
        siloBuilder.AddMemoryGrainStorage("PointStore");
        siloBuilder.AddMemoryStreams("PointUpdates");
        siloBuilder.AddMemoryGrainStorage("PubSubStore");
    }
}
