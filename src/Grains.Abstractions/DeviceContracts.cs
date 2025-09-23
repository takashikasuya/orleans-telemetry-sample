using Orleans;
using Orleans.Streams;

namespace Grains.Abstractions;

/// <summary>
/// Immutable data transfer object representing a telemetry message consumed
/// from the message queue.  The upstream publisher must guarantee that
/// sequences are monotonic for a given device; otherwise the grain will
/// ignore out‑of‑order events.
/// </summary>
[GenerateSerializer]
public class TelemetryMsg
{
    /// <summary>
    /// The unique identifier for the device that generated the telemetry message.
    /// </summary>
    [Id(0)]
    public string DeviceId { get; set; } = default!;

    /// <summary>
    /// The sequence number of the telemetry message, used to indicate the order
    /// of messages from the same device.
    /// </summary>
    [Id(1)]
    public int Sequence { get; set; }

    /// <summary>
    /// A dictionary of additional properties associated with the telemetry message.
    /// </summary>
    [Id(2)]
    public Dictionary<string, object> Properties { get; set; } = new();

    /// <summary>
    /// The tenant identifier for multi-tenant support.
    /// </summary>
    [Id(3)]
    public string TenantId { get; set; } = default!;

    /// <summary>
    /// The timestamp when the telemetry message was generated.
    /// </summary>
    [Id(4)]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TelemetryMsg"/> class with all properties.
    /// </summary>
    /// <param name="TenantId">The tenant identifier for multi-tenant support.</param>
    /// <param name="DeviceId">The unique identifier for the device that generated the telemetry message.</param>
    /// <param name="Sequence">The sequence number of the telemetry message.</param>
    /// <param name="Timestamp">The timestamp when the telemetry message was generated.</param>
    /// <param name="Properties">A dictionary of additional properties associated with the telemetry message.</param>
    public TelemetryMsg(string TenantId, string DeviceId, long Sequence, DateTimeOffset Timestamp, Dictionary<string, object> Properties)
    {
        this.TenantId = TenantId;
        this.DeviceId = DeviceId;
        this.Sequence = (int)Sequence;
        this.Timestamp = Timestamp;
        this.Properties = Properties;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TelemetryMsg"/> class.
    /// </summary>
    public TelemetryMsg() { }
}

/// <summary>
/// Snapshot returned by the grain representing the latest state for a device.
/// </summary>
public sealed record DeviceSnapshot(
    long LastSequence,
    IReadOnlyDictionary<string, object> LatestProps,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Grain interface for device state management.  Each device is keyed by
/// "{tenant}:{deviceId}" to support multi‑tenant isolation.
/// </summary>
public interface IDeviceGrain : IGrainWithStringKey
{
    /// <summary>Applies an incoming telemetry message to the grain state.</summary>
    Task UpsertAsync(TelemetryMsg msg);

    /// <summary>Reads the current snapshot from the grain.</summary>
    Task<DeviceSnapshot> GetAsync();
}
