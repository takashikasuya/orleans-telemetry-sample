using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Grains.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using Telemetry.Ingest;

namespace Telemetry.Ingest.Mqtt;

public sealed class MqttIngestConnector : ITelemetryIngestConnector, IAsyncDisposable
{
    private readonly MqttIngestOptions _options;
    private readonly ILogger<MqttIngestConnector> _logger;
    private readonly List<CompiledBinding> _bindings;
    private IMqttClient? _client;
    private long _sequence;

    public MqttIngestConnector(IOptions<MqttIngestOptions> options, ILogger<MqttIngestConnector> logger)
    {
        _options = options.Value;
        _logger = logger;
        _bindings = CompileBindings(_options.TopicBindings);
    }

    public string Name => "Mqtt";

    public async Task StartAsync(ChannelWriter<TelemetryPointMsg> writer, CancellationToken ct)
    {
        if (_bindings.Count == 0)
        {
            _logger.LogWarning("MQTT connector has no topic bindings. connector={Connector}", Name);
            await Task.Delay(Timeout.Infinite, ct);
            return;
        }

        var reconnectDelayMs = Math.Max(100, _options.ReconnectDelayMs);
        var maxReconnectDelayMs = Math.Max(reconnectDelayMs, _options.MaxReconnectDelayMs);

        while (!ct.IsCancellationRequested)
        {
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _client = new MqttFactory().CreateMqttClient();
                _client.ApplicationMessageReceivedAsync += args => HandleMessageAsync(args, writer, ct);
                _client.DisconnectedAsync += _ =>
                {
                    disconnected.TrySetResult();
                    return Task.CompletedTask;
                };

                await _client.ConnectAsync(BuildClientOptions(), ct);
                await SubscribeAsync(_client, ct);
                _logger.LogInformation("MQTT connector connected and subscribed. bindings={Count}", _bindings.Count);

                await Task.WhenAny(disconnected.Task, Task.Delay(Timeout.Infinite, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT connector loop failed. retrying in {DelayMs}ms", reconnectDelayMs);
            }
            finally
            {
                await DisposeClientAsync();
            }

            await Task.Delay(reconnectDelayMs, ct);
            reconnectDelayMs = Math.Min(maxReconnectDelayMs, reconnectDelayMs * 2);
        }
    }

    private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs args, ChannelWriter<TelemetryPointMsg> writer, CancellationToken ct)
    {
        try
        {
            var topic = args.ApplicationMessage.Topic ?? string.Empty;
            var payload = args.ApplicationMessage.PayloadSegment.ToArray();

            if (!TryParseTelemetryPoint(topic, payload, _bindings, _options.Payload, Interlocked.Increment(ref _sequence), DateTimeOffset.UtcNow, out var point, out var reason))
            {
                _logger.LogWarning("MQTT message dropped. topic={Topic}, reason={Reason}", topic, reason);
                return;
            }

            var written = await WriteWithPolicyAsync(writer, point!, _options.DropPolicy, _options.WriteTimeoutMs, ct);
            if (!written)
            {
                _logger.LogWarning("MQTT message dropped by backpressure policy. topic={Topic}", topic);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT message handling failed.");
        }
    }

    private MqttClientOptions BuildClientOptions()
    {
        var builder = new MqttClientOptionsBuilder()
            .WithClientId(string.IsNullOrWhiteSpace(_options.ClientId) ? "telemetry-ingest-mqtt" : _options.ClientId)
            .WithTcpServer(string.IsNullOrWhiteSpace(_options.Host) ? "localhost" : _options.Host, _options.Port)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(Math.Max(5, _options.KeepAliveSeconds)))
            .WithCleanSession(_options.CleanSession)
            .WithReceiveMaximum((ushort)Math.Clamp(_options.MaxInFlightMessages, 1, ushort.MaxValue));

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            builder.WithCredentials(_options.Username, _options.Password);
        }

        return builder.Build();
    }

    internal static List<CompiledBinding> CompileBindings(IReadOnlyCollection<MqttTopicBindingOptions> bindings)
    {
        var result = new List<CompiledBinding>(bindings.Count);
        foreach (var binding in bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.Filter))
            {
                continue;
            }

            result.Add(new CompiledBinding(binding.Filter, ToMqttQos(binding.Qos), binding.CompileRegex()));
        }

        return result;
    }

    internal static bool TryParseTelemetryPoint(
        string topic,
        byte[] payload,
        IReadOnlyList<CompiledBinding> bindings,
        MqttPayloadOptions payloadOptions,
        long sequence,
        DateTimeOffset receivedAt,
        out TelemetryPointMsg? message,
        out string reason)
    {
        message = null;
        reason = string.Empty;

        var binding = bindings.FirstOrDefault(x => x.TopicRegex.IsMatch(topic));
        if (binding is null)
        {
            reason = "topic_regex_no_match";
            return false;
        }

        var match = binding.TopicRegex.Match(topic);
        var tenantId = match.Groups["tenantId"].Value?.Trim();
        var deviceId = match.Groups["deviceId"].Value?.Trim();
        var pointId = match.Groups["pointId"].Value?.Trim();

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(pointId))
        {
            reason = "topic_id_missing";
            return false;
        }

        if (!TryParsePayload(payload, payloadOptions, receivedAt, out var value, out var timestamp, out reason))
        {
            return false;
        }

        message = new TelemetryPointMsg
        {
            TenantId = tenantId,
            DeviceId = deviceId,
            PointId = pointId,
            BuildingName = string.Empty,
            SpaceId = string.Empty,
            Sequence = sequence,
            Timestamp = timestamp,
            Value = value
        };

        return true;
    }

    internal static async Task<bool> WriteWithPolicyAsync(
        ChannelWriter<TelemetryPointMsg> writer,
        TelemetryPointMsg message,
        MqttDropPolicy policy,
        int timeoutMs,
        CancellationToken ct)
    {
        switch (policy)
        {
            case MqttDropPolicy.Block:
                await writer.WriteAsync(message, ct);
                return true;

            case MqttDropPolicy.DropNewest:
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(Math.Max(1, timeoutMs));
                try
                {
                    var canWrite = await writer.WaitToWriteAsync(timeoutCts.Token);
                    if (!canWrite)
                    {
                        return false;
                    }

                    return writer.TryWrite(message);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return false;
                }
            }

            case MqttDropPolicy.FailFast:
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(Math.Max(1, timeoutMs));
                try
                {
                    var canWrite = await writer.WaitToWriteAsync(timeoutCts.Token);
                    if (!canWrite)
                    {
                        throw new TimeoutException("MQTT write failed due to backpressure (channel closed).");
                    }

                    if (!writer.TryWrite(message))
                    {
                        throw new TimeoutException("MQTT write failed due to backpressure.");
                    }

                    return true;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException("MQTT write timed out due to backpressure.");
                }
            }

            default:
                await writer.WriteAsync(message, ct);
                return true;
        }
    }

    private static bool TryParsePayload(
        byte[] payload,
        MqttPayloadOptions options,
        DateTimeOffset fallbackTimestamp,
        out object? value,
        out DateTimeOffset timestamp,
        out string reason)
    {
        value = null;
        timestamp = fallbackTimestamp;
        reason = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (!TryGetByJsonPath(root, options.ValueJsonPath, out var valueElement))
            {
                reason = "payload_value_missing";
                return false;
            }

            value = ConvertJsonElement(valueElement);

            if (TryGetByJsonPath(root, options.DateTimeJsonPath, out var timestampElement))
            {
                if (!TryParseDateTime(timestampElement, options, out timestamp))
                {
                    timestamp = fallbackTimestamp;
                }
            }

            return true;
        }
        catch (Exception)
        {
            reason = "payload_parse_failed";
            return false;
        }
    }

    internal static bool TryGetByJsonPath(JsonElement root, string path, out JsonElement value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("$.", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = path.Substring(2).Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        foreach (var segment in segments)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return false;
            }

            current = next;
        }

        value = current;
        return true;
    }

    internal static bool TryParseDateTime(JsonElement element, MqttPayloadOptions options, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (options.DateTimeFormats.Count > 0)
        {
            foreach (var format in options.DateTimeFormats)
            {
                if (DateTimeOffset.TryParseExact(text, format, null, System.Globalization.DateTimeStyles.RoundtripKind, out timestamp))
                {
                    return true;
                }
            }
        }

        if (DateTimeOffset.TryParse(text, out timestamp))
        {
            return true;
        }

        if (options.AssumeUtc && DateTime.TryParse(text, out var dt))
        {
            timestamp = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
            return true;
        }

        return false;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(x => x.Name, x => ConvertJsonElement(x.Value) as object),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.ToString()
        };
    }

    private static MqttQualityOfServiceLevel ToMqttQos(int qos)
        => qos switch
        {
            <= 0 => MqttQualityOfServiceLevel.AtMostOnce,
            1 => MqttQualityOfServiceLevel.AtLeastOnce,
            _ => MqttQualityOfServiceLevel.ExactlyOnce
        };

    private async Task SubscribeAsync(IMqttClient client, CancellationToken ct)
    {
        foreach (var binding in _bindings)
        {
            await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(binding.Filter).WithQualityOfServiceLevel(binding.Qos))
                .Build(), ct);
        }
    }

    private async ValueTask DisposeClientAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync();
            }
        }
        catch
        {
            // ignore
        }

        _client.Dispose();
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeClientAsync();
    }

    internal sealed record CompiledBinding(string Filter, MqttQualityOfServiceLevel Qos, Regex TopicRegex);
}
