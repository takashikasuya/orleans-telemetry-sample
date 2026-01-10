namespace Telemetry.Storage;

public sealed class TelemetryStorageOptions
{
    public string StagePath { get; set; } = "storage/stage";

    public string ParquetPath { get; set; } = "storage/parquet";

    public string IndexPath { get; set; } = "storage/index";

    public int BucketMinutes { get; set; } = 15;

    public int CompactionIntervalSeconds { get; set; } = 300;

    public int DefaultQueryLimit { get; set; } = 1000;
}
