using System.Text.Json;
using Grains.Abstractions;
using RabbitMQ.Client;

// Publisher sends random telemetry messages periodically for a few
// devices.  This app is purely for demonstration and should not be
// considered production ready.
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

var devices = Enumerable.Range(1, Math.Max(1, deviceCount)).Select(i => $"dev-{i}").ToArray();
var tenant = Environment.GetEnvironmentVariable("TENANT_ID") ?? "t1";
var buildingName = Environment.GetEnvironmentVariable("BUILDING_NAME") ?? "bldg-1";
var spaceId = Environment.GetEnvironmentVariable("SPACE_ID") ?? "floor-1/room-1";
var rand = new Random();

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

var seqs = new Dictionary<string, long>();
foreach (var d in devices) seqs[d] = 0;

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

    foreach (var dev in devices)
    {
        if (seqs[dev] == long.MaxValue)
        {
            seqs[dev] = 0;
        }
        else
        {
            seqs[dev]++;
        }

        var seq = seqs[dev];
        var msg = new TelemetryMsg(
            TenantId: tenant,
            DeviceId: dev,
            Sequence: seq,
            Timestamp: DateTimeOffset.UtcNow,
            Properties: new Dictionary<string, object>
            {
                ["temperature"] = 20 + rand.NextDouble() * 10,
                ["humidity"] = 50 + rand.NextDouble() * 20
            },
            BuildingName: buildingName,
            SpaceId: spaceId
        );
        var body = JsonSerializer.SerializeToUtf8Bytes(msg);
        var props = channel.CreateBasicProperties();
        props.Persistent = true;
        channel.BasicPublish(exchange: "", routingKey: "telemetry", basicProperties: props, body: body);
        Console.WriteLine($"Published {dev} seq {seq}");
        await Task.Delay(inBurst ? burstInterval : baseInterval);
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
