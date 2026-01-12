using System;
using System.Linq;
using System.Net;
using DataModel.Analyzer.Extensions;
using Grains.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Streaming;
using Telemetry.Ingest;
using Telemetry.Ingest.Kafka;
using Telemetry.Ingest.RabbitMq;
using Telemetry.Ingest.Simulator;
using Telemetry.Storage;

namespace SiloHost;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices((context, services) =>
        {
            services.AddDataModelAnalyzer();
            services.AddSingleton<ITelemetryRouterGrain>(provider =>
            {
                var grainFactory = provider.GetRequiredService<IGrainFactory>();
                return grainFactory.GetGrain<ITelemetryRouterGrain>(Guid.Empty);
            });
            var ingestSection = context.Configuration.GetSection("TelemetryIngest");
            var storageSection = context.Configuration.GetSection("TelemetryStorage");
            services.AddTelemetryIngest(ingestSection);
            // Connector registration stays in code; config controls which ones are enabled.
            services.AddKafkaIngest(ingestSection.GetSection("Kafka"));
            services.AddRabbitMqIngest(ingestSection.GetSection("RabbitMq"));
            services.AddSimulatorIngest(ingestSection.GetSection("Simulator"));
            services.AddLoggingTelemetryEventSink();
            services.Configure<TelemetryStorageOptions>(storageSection);
            services.AddTelemetryStorage();
            services.AddSingleton<GraphSeeder>();
            services.AddHostedService<GraphSeedService>();
        });
        builder.UseOrleans((context, siloBuilder) =>
        {
            var orleansSection = context.Configuration.GetSection("Orleans");
            var advertisedHost = orleansSection["AdvertisedIPAddress"];
            var siloPort = orleansSection.GetValue("SiloPort", 11111);
            var gatewayPort = orleansSection.GetValue("GatewayPort", 30000);

            // use localhost clustering for dev; override advertised address for Docker when configured
            siloBuilder.UseLocalhostClustering(siloPort: siloPort, gatewayPort: gatewayPort);
            if (!string.IsNullOrWhiteSpace(advertisedHost))
            {
                var advertisedAddress = ResolveAdvertisedAddress(advertisedHost);
                siloBuilder.Configure<EndpointOptions>(options =>
                {
                    options.AdvertisedIPAddress = advertisedAddress;
                    options.SiloPort = siloPort;
                    options.GatewayPort = gatewayPort;
                    options.SiloListeningEndpoint = new IPEndPoint(IPAddress.Any, siloPort);
                    options.GatewayListeningEndpoint = new IPEndPoint(IPAddress.Any, gatewayPort);
                });
            }
            siloBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = "telemetry-cluster";
                options.ServiceId = "telemetry-service";
            });
            // configure grain storage
            siloBuilder.AddMemoryGrainStorage("DeviceStore");
            siloBuilder.AddMemoryGrainStorage("GraphStore");
            siloBuilder.AddMemoryGrainStorage("GraphIndexStore");
            siloBuilder.AddMemoryStreams("DeviceUpdates");
            siloBuilder.AddMemoryGrainStorage("PointStore");
            siloBuilder.AddMemoryStreams("PointUpdates");
            // add stream provider for device updates
            // siloBuilder.AddSimpleMessageStreamProvider("DeviceUpdates");
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
        });
        var host = builder.Build();
        await host.RunAsync();
    }

    private static IPAddress ResolveAdvertisedAddress(string host)
    {
        if (IPAddress.TryParse(host, out var parsed))
        {
            return parsed;
        }

        return Dns.GetHostAddresses(host).First(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
    }
}
