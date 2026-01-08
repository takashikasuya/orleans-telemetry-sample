using System;
using Grains.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Streaming;

namespace SiloHost;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ITelemetryRouterGrain>(provider =>
            {
                var grainFactory = provider.GetRequiredService<IGrainFactory>();
                return grainFactory.GetGrain<ITelemetryRouterGrain>(Guid.Empty);
            });
            services.AddHostedService<MqIngestService>();
        });
        builder.UseOrleans(siloBuilder =>
        {
            // use localhost clustering for dev; in production configure appropriately
            siloBuilder.UseLocalhostClustering();
            siloBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = "telemetry-cluster";
                options.ServiceId = "telemetry-service";
            });
            // configure grain storage
            siloBuilder.AddMemoryGrainStorage("DeviceStore");
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
}
