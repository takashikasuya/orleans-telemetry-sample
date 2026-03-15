using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Telemetry.Ingest.RabbitMq;

public sealed class RabbitMqControlEgressConnector : IControlEgressConnector, IDisposable
{
    private readonly RabbitMqControlEgressOptions _options;
    private readonly ILogger<RabbitMqControlEgressConnector> _logger;
    private IConnection? _connection;
    private IModel? _model;
    private readonly object _lock = new();

    public RabbitMqControlEgressConnector(
        IOptions<RabbitMqControlEgressOptions> options,
        ILogger<RabbitMqControlEgressConnector> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "RabbitMq";

    public ControlConfirmMode ConfirmMode => ControlConfirmMode.AckOnly;

    public Task<ControlEgressResult> SendAsync(ControlEgressRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            EnsureConnection();
            var queueName = ResolveQueueName();
            var body = SerializeRequest(request);
            _model!.BasicPublish(exchange: string.Empty, routingKey: queueName, body: body);

            _logger.LogInformation(
                "Control command {CommandId} published to RabbitMQ queue {Queue}",
                request.CommandId,
                queueName);

            return Task.FromResult(new ControlEgressResult
            {
                CommandId = request.CommandId,
                ConnectorName = Name,
                Accepted = true,
                ConfirmMode = ConfirmMode,
                CorrelationId = request.CommandId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish control command {CommandId} to RabbitMQ",
                request.CommandId);

            return Task.FromResult(new ControlEgressResult
            {
                CommandId = request.CommandId,
                ConnectorName = Name,
                Accepted = false,
                Error = ex.Message,
                ConfirmMode = ConfirmMode
            });
        }
    }

    internal static byte[] SerializeRequest(ControlEgressRequest request)
    {
        var payload = new
        {
            deviceId = request.DeviceId,
            pointId = request.PointId,
            value = request.DesiredValue,
            commandId = request.CommandId,
            tenantId = request.TenantId,
            buildingName = request.BuildingName,
            spaceId = request.SpaceId,
            requestedAt = request.RequestedAt,
            metadata = request.Metadata
        };

        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    internal void EnsureConnection()
    {
        if (_connection is { IsOpen: true })
        {
            return;
        }

        lock (_lock)
        {
            if (_connection is { IsOpen: true })
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
        }
    }

    private string ResolveHostName()
        => string.IsNullOrWhiteSpace(_options.HostName)
            ? Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "mq"
            : _options.HostName;

    private int ResolvePort()
    {
        if (_options.Port.HasValue)
        {
            return _options.Port.Value;
        }

        return int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out var port) ? port : 5672;
    }

    private string ResolveUserName()
        => string.IsNullOrWhiteSpace(_options.UserName)
            ? Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "user"
            : _options.UserName;

    private string ResolvePassword()
        => string.IsNullOrWhiteSpace(_options.Password)
            ? Environment.GetEnvironmentVariable("RABBITMQ_PASS") ?? "password"
            : _options.Password;

    private string ResolveQueueName()
        => string.IsNullOrWhiteSpace(_options.QueueName) ? "telemetry-control" : _options.QueueName;

    public void Dispose()
    {
        _model?.Close();
        _model?.Dispose();
        _model = null;
        _connection?.Close();
        _connection?.Dispose();
        _connection = null;
    }
}
