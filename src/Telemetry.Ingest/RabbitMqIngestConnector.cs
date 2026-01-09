using System.Text.Json;
using System.Threading.Channels;
using Grains.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Telemetry.Ingest;

namespace Telemetry.Ingest.RabbitMq;

public sealed class RabbitMqIngestConnector : ITelemetryIngestConnector, IAsyncDisposable
{
    private readonly RabbitMqIngestOptions _options;
    private readonly ILogger<RabbitMqIngestConnector> _logger;
    private IConnection? _connection;
    private IModel? _model;

    public RabbitMqIngestConnector(
        IOptions<RabbitMqIngestOptions> options,
        ILogger<RabbitMqIngestConnector> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "RabbitMq";

    public async Task StartAsync(ChannelWriter<TelemetryPointMsg> writer, CancellationToken ct)
    {
        EnsureConnection();
        var queueName = ResolveQueueName();

        var consumer = new AsyncEventingBasicConsumer(_model);
        consumer.Received += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var msg = JsonSerializer.Deserialize<TelemetryMsg>(body);
                if (msg is null)
                {
                    _model!.BasicNack(ea.DeliveryTag, false, false);
                    return;
                }

                foreach (var kv in msg.Properties)
                {
                    var pointMsg = new TelemetryPointMsg
                    {
                        TenantId = msg.TenantId,
                        BuildingName = msg.BuildingName,
                        SpaceId = msg.SpaceId,
                        DeviceId = msg.DeviceId,
                        PointId = kv.Key,
                        Sequence = msg.Sequence,
                        Timestamp = msg.Timestamp,
                        Value = kv.Value
                    };
                    await writer.WriteAsync(pointMsg, ct);
                }

                _model!.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "failed to process message");
                _model!.BasicNack(ea.DeliveryTag, false, false);
            }
        };

        _model!.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);

        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void EnsureConnection()
    {
        if (_connection is not null)
        {
            return;
        }

        var factory = new ConnectionFactory
        {
            HostName = ResolveHostName(),
            Port = ResolvePort(),
            UserName = ResolveUserName(),
            Password = ResolvePassword(),
            DispatchConsumersAsync = true
        };
        _connection = factory.CreateConnection();
        _model = _connection.CreateModel();
        _model.QueueDeclare(queue: ResolveQueueName(), durable: false, exclusive: false, autoDelete: false);
        _model.BasicQos(prefetchSize: 0, prefetchCount: ResolvePrefetchCount(), global: false);
    }

    private string ResolveHostName()
    {
        return string.IsNullOrWhiteSpace(_options.HostName)
            ? Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "mq"
            : _options.HostName;
    }

    private int ResolvePort()
    {
        if (_options.Port.HasValue)
        {
            return _options.Port.Value;
        }

        return int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out var port)
            ? port
            : 5672;
    }

    private string ResolveUserName()
    {
        return string.IsNullOrWhiteSpace(_options.UserName)
            ? Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "user"
            : _options.UserName;
    }

    private string ResolvePassword()
    {
        return string.IsNullOrWhiteSpace(_options.Password)
            ? Environment.GetEnvironmentVariable("RABBITMQ_PASS") ?? "password"
            : _options.Password;
    }

    private string ResolveQueueName()
    {
        return string.IsNullOrWhiteSpace(_options.QueueName) ? "telemetry" : _options.QueueName;
    }

    private ushort ResolvePrefetchCount()
    {
        return _options.PrefetchCount == 0 ? (ushort)100 : _options.PrefetchCount;
    }

    public ValueTask DisposeAsync()
    {
        _model?.Close();
        _model?.Dispose();
        _model = null;
        _connection?.Close();
        _connection?.Dispose();
        _connection = null;
        return ValueTask.CompletedTask;
    }
}
