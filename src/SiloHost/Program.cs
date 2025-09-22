using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Grains.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Streaming;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SiloHost;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureServices(services =>
        {
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
            // add stream provider for device updates
            // siloBuilder.AddSimpleMessageStreamProvider("DeviceUpdates");
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
        });
        var host = builder.Build();
        await host.RunAsync();
    }
}

/// <summary>
/// Background service that consumes messages from RabbitMQ and dispatches
/// them to the telemetry router grain via an in‑memory bounded channel.  To
/// adjust throughput tune the channel size and batch size.
/// </summary>
internal sealed class MqIngestService : BackgroundService
{
    private readonly IGrainFactory _grains;
    private readonly ILogger<MqIngestService> _logger;
    private readonly Channel<TelemetryMsg> _channel;
    private IConnection? _connection;
    private IModel? _model;
    public MqIngestService(IGrainFactory grains, ILogger<MqIngestService> logger)
    {
        _grains = grains;
        _logger = logger;
        _channel = Channel.CreateBounded<TelemetryMsg>(new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // start consumer and router loops
        var consumeTask = ConsumeLoopAsync(stoppingToken);
        var routeTask = RouteLoopAsync(stoppingToken);
        await Task.WhenAll(consumeTask, routeTask);
    }
    private void EnsureConnection()
    {
        if (_connection is not null) return;
        var factory = new ConnectionFactory
        {
            //HostName = "localhost",
            HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "mq",
            // TODO: use secure credentials in production
            UserName = "user",
            Password = "password",
            DispatchConsumersAsync = true
        };
        _connection = factory.CreateConnection();
        _model = _connection.CreateModel();
        _model.QueueDeclare(queue: "telemetry", durable: false, exclusive: false, autoDelete: false);
        _model.BasicQos(prefetchSize: 0, prefetchCount: 100, global: false);
    }
    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        EnsureConnection();
        var consumer = new AsyncEventingBasicConsumer(_model);
        consumer.Received += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var msg = JsonSerializer.Deserialize<TelemetryMsg>(body);
                if (msg is not null)
                {
                    await _channel.Writer.WriteAsync(msg, ct);
                    _model!.BasicAck(ea.DeliveryTag, multiple: false);
                }
                else
                {
                    _model!.BasicNack(ea.DeliveryTag, false, false);
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation so shutdown can proceed without noise.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "failed to process message");
                _model!.BasicNack(ea.DeliveryTag, false, false);
            }
        };
        _model.BasicConsume(queue: "telemetry", autoAck: false, consumer: consumer);
        // wait until canceled
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _channel.Writer.TryComplete();
        }
    }
    private async Task RouteLoopAsync(CancellationToken ct)
    {
        var router = _grains.GetGrain<ITelemetryRouterGrain>(Guid.Empty);
        var batch = new List<TelemetryMsg>(100);
        try
        {
            await foreach (var msg in _channel.Reader.ReadAllAsync(ct))
            {
                batch.Add(msg);
                if (batch.Count < 100)
                {
                    continue;
                }

                await router.RouteBatchAsync(batch.ToArray());
                batch.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            // Allow graceful shutdown when cancellation is requested.
        }
        finally
        {
            if (batch.Count > 0)
            {
                await router.RouteBatchAsync(batch.ToArray());
                batch.Clear();
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        _model?.Close();
        _model?.Dispose();
        _model = null;
        _connection?.Close();
        _connection?.Dispose();
        _connection = null;
        await base.StopAsync(cancellationToken);
    }
}
