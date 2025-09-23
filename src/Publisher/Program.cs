using System.Text;
using System.Text.Json;
using Grains.Abstractions;
using RabbitMQ.Client;

// Publisher sends random telemetry messages periodically for a few
// devices.  This app is purely for demonstration and should not be
// considered production ready.
var devices = new[] { "dev-1", "dev-2", "dev-3" };
var tenant = Environment.GetEnvironmentVariable("TENANT") ?? "t1";
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

while (true)
{
    foreach (var dev in devices)
    {
        var seq = ++seqs[dev];
        var msg = new TelemetryMsg(
            TenantId: tenant,
            DeviceId: dev,
            Sequence: seq,
            Timestamp: DateTimeOffset.UtcNow,
            Properties: new Dictionary<string, object>
            {
                ["temperature"] = 20 + rand.NextDouble() * 10,
                ["humidity"] = 50 + rand.NextDouble() * 20
            }
        );
        var body = JsonSerializer.SerializeToUtf8Bytes(msg);
        var props = channel.CreateBasicProperties();
        props.Persistent = true;
        channel.BasicPublish(exchange: "", routingKey: "telemetry", basicProperties: props, body: body);
        Console.WriteLine($"Published {dev} seq {seq}");
        await Task.Delay(500);
    }
}
