using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataModel.Analyzer.Services;
using Grains.Abstractions;
using Microsoft.Extensions.Logging;
using Publisher;
using RabbitMQ.Client;

var deviceCount = GetIntArgValue(args, "--device-count")
    ?? GetIntEnvValue("DEVICE_COUNT")
    ?? 3;
var baseIntervalMs = GetIntArgValue(args, "--interval-ms")
    ?? GetIntEnvValue("PUBLISH_INTERVAL_MS")
    ?? 500;
var burstEnabled = args.Contains("--burst", StringComparer.OrdinalIgnoreCase)
    || GetBoolEnvValue("BURST_ENABLED");
var burstIntervalMs = GetIntArgValue(args, "--burst-interval-ms")
    ?? GetIntEnvValue("BURST_INTERVAL_MS")
    ?? 5;
var burstDurationSeconds = GetIntArgValue(args, "--burst-duration-sec")
    ?? GetIntEnvValue("BURST_DURATION_SEC")
    ?? 10;
var burstPauseSeconds = GetIntArgValue(args, "--burst-pause-sec")
    ?? GetIntEnvValue("BURST_PAUSE_SEC")
    ?? 20;

var randomDeviceIds = Enumerable.Range(1, Math.Max(1, deviceCount))
    .Select(i => $"dev-{i}")
    .ToArray();
var tenant = Environment.GetEnvironmentVariable("TENANT_ID") ?? "t1";
var buildingName = Environment.GetEnvironmentVariable("BUILDING_NAME") ?? "bldg-1";
var spaceId = Environment.GetEnvironmentVariable("SPACE_ID") ?? "floor-1/room-1";
var rand = new Random();

var rdfPath = Environment.GetEnvironmentVariable("RDF_SEED_PATH");
using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole());
var logger = loggerFactory.CreateLogger("Publisher");
var rdfGenerator = await TryCreateRdfGeneratorAsync(rdfPath, logger, loggerFactory);
var useRdf = rdfGenerator is not null && rdfGenerator.DeviceCount > 0;
var rdfDevices = rdfGenerator?.Devices ?? Array.Empty<RdfTelemetryGenerator.RdfDeviceDefinition>();

var factory = new ConnectionFactory
{
    HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost",
    Port = int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out var port) ? port : 5672,
    UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "user",
    Password = Environment.GetEnvironmentVariable("RABBITMQ_PASS") ?? "password",
};
using var conn = factory.CreateConnection();
using var channel = conn.CreateModel();
channel.QueueDeclare(queue: "telemetry", durable: false, exclusive: false, autoDelete: false);

var sequences = new Dictionary<string, long>();

var useBurst = burstEnabled && burstIntervalMs > 0;
var baseInterval = TimeSpan.FromMilliseconds(Math.Max(1, baseIntervalMs));
var burstInterval = TimeSpan.FromMilliseconds(Math.Max(1, burstIntervalMs));
var burstDuration = TimeSpan.FromSeconds(Math.Max(1, burstDurationSeconds));
var burstPause = TimeSpan.FromSeconds(Math.Max(1, burstPauseSeconds));
var nextBurstSwitch = DateTimeOffset.UtcNow + burstPause;
var inBurst = false;

while (true)
{
    if (useBurst && DateTimeOffset.UtcNow >= nextBurstSwitch)
    {
        inBurst = !inBurst;
        nextBurstSwitch = DateTimeOffset.UtcNow + (inBurst ? burstDuration : burstPause);
    }

    if (useRdf)
    {
        foreach (var device in rdfDevices)
        {
            var seq = NextSequence(sequences, device.DeviceId);
            var msg = rdfGenerator!.CreateTelemetry(tenant, device, seq);
            PublishMessage(channel, msg);
            await Task.Delay(inBurst ? burstInterval : baseInterval);
        }
    }
    else
    {
        foreach (var deviceId in randomDeviceIds)
        {
            var seq = NextSequence(sequences, deviceId);
            var msg = BuildRandomTelemetryMsg(tenant, deviceId, seq, rand, buildingName, spaceId);
            PublishMessage(channel, msg);
            await Task.Delay(inBurst ? burstInterval : baseInterval);
        }
    }
}

static TelemetryMsg BuildRandomTelemetryMsg(string tenantId, string deviceId, long sequence, Random rand, string buildingName, string spaceId)
{
    return new TelemetryMsg(
        TenantId: tenantId,
        DeviceId: deviceId,
        Sequence: sequence,
        Timestamp: DateTimeOffset.UtcNow,
        Properties: new Dictionary<string, object>
        {
            ["temperature"] = 20 + rand.NextDouble() * 10,
            ["humidity"] = 50 + rand.NextDouble() * 20
        },
        BuildingName: buildingName,
        SpaceId: spaceId
    );
}

static long NextSequence(Dictionary<string, long> sequences, string deviceId)
{
    if (!sequences.TryGetValue(deviceId, out var value))
    {
        value = 0;
    }

    if (value == long.MaxValue)
    {
        value = 0;
    }
    else
    {
        value++;
    }

    sequences[deviceId] = value;
    return value;
}

static void PublishMessage(IModel channel, TelemetryMsg msg)
{
    var body = JsonSerializer.SerializeToUtf8Bytes(msg);
    var props = channel.CreateBasicProperties();
    props.Persistent = true;
    channel.BasicPublish(exchange: "", routingKey: "telemetry", basicProperties: props, body: body);
    Console.WriteLine($"Published {msg.DeviceId} seq {msg.Sequence}");
}

static async Task<RdfTelemetryGenerator?> TryCreateRdfGeneratorAsync(string? path, ILogger logger, ILoggerFactory loggerFactory)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        logger.LogInformation("RDF_SEED_PATH is not configured, falling back to random telemetry.");
        return null;
    }

    if (!File.Exists(path))
    {
        logger.LogWarning("RDF file {Path} was not found, falling back to random telemetry.", path);
        return null;
    }

    try
    {
        var analyzer = new RdfAnalyzerService(loggerFactory.CreateLogger<RdfAnalyzerService>());
        var model = await analyzer.AnalyzeRdfFileAsync(path);
        var generator = new RdfTelemetryGenerator(model);
        logger.LogInformation("Loaded RDF seed {Path} ({DeviceCount} devices, {PointCount} points).", path, generator.DeviceCount, generator.PointCount);
        return generator;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to parse RDF seed {Path}, continuing with random telemetry.", path);
        return null;
    }
}

static int? GetIntArgValue(string[] args, string key)
{
    var value = GetArgValue(args, key);
    return int.TryParse(value, out var parsed) ? parsed : null;
}

static string? GetArgValue(string[] args, string key)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static int? GetIntEnvValue(string key)
{
    return int.TryParse(Environment.GetEnvironmentVariable(key), out var value) ? value : null;
}

static bool GetBoolEnvValue(string key)
{
    var value = Environment.GetEnvironmentVariable(key);
    return bool.TryParse(value, out var parsed) && parsed;
}
