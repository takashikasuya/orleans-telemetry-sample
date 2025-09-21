using Orleans;
using Orleans.Streams;

namespace Grains.Abstractions;

/// <summary>
/// Immutable data transfer object representing a telemetry message consumed
/// from the message queue.  The upstream publisher must guarantee that
/// sequences are monotonic for a given device; otherwise the grain will
/// ignore out‑of‑order events.
/// </summary>
public sealed record TelemetryMsg(
    string TenantId,
    string DeviceId,
    long Sequence,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, object> Properties);

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
