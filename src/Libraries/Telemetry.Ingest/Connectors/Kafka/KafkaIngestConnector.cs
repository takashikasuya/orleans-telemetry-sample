using System.Text.Json;
using System.Threading.Channels;
using Confluent.Kafka;
using Grains.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telemetry.Ingest;

namespace Telemetry.Ingest.Kafka;

public sealed class KafkaIngestConnector : ITelemetryIngestConnector, IAsyncDisposable
{
    private readonly KafkaIngestOptions _options;
    private readonly ILogger<KafkaIngestConnector> _logger;
    private IConsumer<Ignore, byte[]>? _consumer;

    public KafkaIngestConnector(
        IOptions<KafkaIngestOptions> options,
        ILogger<KafkaIngestConnector> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "Kafka";

    public async Task StartAsync(ChannelWriter<TelemetryPointMsg> writer, CancellationToken ct)
    {
        EnsureConsumer();
        var topic = ResolveTopic();
        _consumer!.Subscribe(topic);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<Ignore, byte[]>? result;
                try
                {
                    result = _consumer.Consume(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume failed.");
                    continue;
                }

                if (result?.Message?.Value is null)
                {
                    _logger.LogWarning("Kafka message payload was empty.");
                    Commit(result);
                    continue;
                }

                if (!TryDeserializeTelemetry(result.Message.Value, out var msg, out var deserializeException))
                {
                    if (deserializeException is not null)
                    {
                        _logger.LogError(deserializeException, "Failed to deserialize Kafka message.");
                    }
                    else
                    {
                        _logger.LogWarning("Kafka message deserialized to null.");
                    }

                    Commit(result);
                    continue;
                }

                foreach (var pointMsg in ToTelemetryPointMessages(msg!))
                {
                    await writer.WriteAsync(pointMsg, ct);
                }

                Commit(result);
            }
        }
        finally
        {
            _consumer?.Close();
        }
    }

    internal static bool TryDeserializeTelemetry(byte[] payload, out TelemetryMsg? message, out Exception? exception)
    {
        try
        {
            message = JsonSerializer.Deserialize<TelemetryMsg>(payload);
            exception = null;
            return message is not null;
        }
        catch (Exception ex)
        {
            message = null;
            exception = ex;
            return false;
        }
    }

    internal static IReadOnlyList<TelemetryPointMsg> ToTelemetryPointMessages(TelemetryMsg message)
    {
        var result = new List<TelemetryPointMsg>(message.Properties.Count);
        foreach (var kv in message.Properties)
        {
            result.Add(new TelemetryPointMsg
            {
                TenantId = message.TenantId,
                BuildingName = message.BuildingName,
                SpaceId = message.SpaceId,
                DeviceId = message.DeviceId,
                PointId = kv.Key,
                Sequence = message.Sequence,
                Timestamp = message.Timestamp,
                Value = kv.Value
            });
        }

        return result;
    }

    private void EnsureConsumer()
    {
        if (_consumer is not null)
        {
            return;
        }

        var config = new ConsumerConfig
        {
            BootstrapServers = ResolveBootstrapServers(),
            GroupId = ResolveGroupId(),
            EnableAutoCommit = _options.EnableAutoCommit,
            AutoOffsetReset = ResolveAutoOffsetReset()
        };

        if (_options.SessionTimeoutMs.HasValue)
        {
            config.SessionTimeoutMs = _options.SessionTimeoutMs.Value;
        }

        if (_options.MaxPollIntervalMs.HasValue)
        {
            config.MaxPollIntervalMs = _options.MaxPollIntervalMs.Value;
        }

        _consumer = new ConsumerBuilder<Ignore, byte[]>(config).Build();
    }

    private string ResolveBootstrapServers()
    {
        return string.IsNullOrWhiteSpace(_options.BootstrapServers)
            ? Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? "localhost:9092"
            : _options.BootstrapServers;
    }

    private string ResolveGroupId()
    {
        return string.IsNullOrWhiteSpace(_options.GroupId)
            ? Environment.GetEnvironmentVariable("KAFKA_GROUP_ID") ?? "telemetry-ingest"
            : _options.GroupId;
    }

    private string ResolveTopic()
    {
        return string.IsNullOrWhiteSpace(_options.Topic)
            ? Environment.GetEnvironmentVariable("KAFKA_TOPIC") ?? "telemetry"
            : _options.Topic;
    }

    private AutoOffsetReset ResolveAutoOffsetReset()
    {
        var value = _options.AutoOffsetReset;
        if (string.IsNullOrWhiteSpace(value))
        {
            value = Environment.GetEnvironmentVariable("KAFKA_AUTO_OFFSET_RESET") ?? "Latest";
        }

        return Enum.TryParse<AutoOffsetReset>(value, ignoreCase: true, out var result)
            ? result
            : AutoOffsetReset.Latest;
    }

    private void Commit(ConsumeResult<Ignore, byte[]>? result)
    {
        if (result is null)
        {
            return;
        }

        try
        {
            _consumer?.Commit(result);
        }
        catch (KafkaException ex)
        {
            _logger.LogError(ex, "Kafka offset commit failed.");
        }
    }

    public ValueTask DisposeAsync()
    {
        _consumer?.Dispose();
        _consumer = null;
        return ValueTask.CompletedTask;
    }
}
