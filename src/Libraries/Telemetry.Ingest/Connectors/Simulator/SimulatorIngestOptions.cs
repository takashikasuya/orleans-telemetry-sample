namespace Telemetry.Ingest.Simulator;

public sealed class SimulatorIngestOptions
{
    public string TenantId { get; set; } = "tenant";

    public string BuildingName { get; set; } = "building";

    public string SpaceId { get; set; } = "space";

    public string DeviceIdPrefix { get; set; } = "device";

    public int DeviceCount { get; set; } = 1;

    public int PointsPerDevice { get; set; } = 1;

    public int IntervalMilliseconds { get; set; } = 2000;
}
