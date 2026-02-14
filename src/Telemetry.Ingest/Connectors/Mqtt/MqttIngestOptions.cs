using System.Text.RegularExpressions;

namespace Telemetry.Ingest.Mqtt;

public sealed class MqttIngestOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 1883;
    public string? ClientId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool CleanSession { get; set; } = true;
    public int KeepAliveSeconds { get; set; } = 30;
    public int ReconnectDelayMs { get; set; } = 2000;
    public int MaxReconnectDelayMs { get; set; } = 30000;
    public List<MqttTopicBindingOptions> TopicBindings { get; set; } = new();
    public MqttPayloadOptions Payload { get; set; } = new();
    public MqttDropPolicy DropPolicy { get; set; } = MqttDropPolicy.Block;
    public int WriteTimeoutMs { get; set; } = 3000;
    public int MaxInFlightMessages { get; set; } = 100;
}

public sealed class MqttTopicBindingOptions
{
    public string Filter { get; set; } = "tenants/+/devices/+/points/+";
    public int Qos { get; set; }
    public string TopicRegex { get; set; } = "^tenants/(?<tenantId>[^/]+)/devices/(?<deviceId>[^/]+)/points/(?<pointId>[^/]+)$";

    internal Regex CompileRegex() => new(TopicRegex, RegexOptions.Compiled | RegexOptions.CultureInvariant);
}

public sealed class MqttPayloadOptions
{
    public string ValueJsonPath { get; set; } = "$.value";
    public string DateTimeJsonPath { get; set; } = "$.datetime";
    public List<string> DateTimeFormats { get; set; } = new() { "O" };
    public bool AssumeUtc { get; set; } = true;
}

public enum MqttDropPolicy
{
    Block,
    DropNewest,
    FailFast
}
