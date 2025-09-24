using System.Text.Json;
//using Devices.V1;
using Grains.Abstractions;

namespace ApiGateway.Telemetry;

internal static class DeviceSnapshotMapper
{
    /*
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static Snapshot ToGrpc(string deviceId, DeviceSnapshot snapshot)
    {
        var proto = new Snapshot
        {
            DeviceId = deviceId,
            LastSequence = snapshot.LastSequence,
            UpdatedAtIso = snapshot.UpdatedAt.ToUniversalTime().ToString("O")
        };

        foreach (var kv in snapshot.LatestProps)
        {
            proto.Properties.Add(new Property
            {
                Key = kv.Key,
                ValueJson = JsonSerializer.Serialize(kv.Value, SerializerOptions)
            });
        }

        return proto;
    }
    */
}
