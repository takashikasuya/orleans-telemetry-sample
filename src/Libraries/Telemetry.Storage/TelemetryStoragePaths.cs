using System.Globalization;

namespace Telemetry.Storage;

public static class TelemetryStoragePaths
{
    public static DateTimeOffset GetBucketStart(DateTimeOffset timestamp, int bucketMinutes)
    {
        var utc = timestamp.ToUniversalTime();
        var bucketMinute = (utc.Minute / bucketMinutes) * bucketMinutes;
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, bucketMinute, 0, TimeSpan.Zero);
    }

    public static string BuildStageFilePath(
        string stageRoot,
        string tenantId,
        string deviceId,
        DateTimeOffset bucketStart)
    {
        var directory = BuildBucketDirectory(stageRoot, tenantId, deviceId, bucketStart);
        return Path.Combine(directory, $"telemetry_{bucketStart:yyyyMMdd_HHmm}.jsonl");
    }

    public static string BuildParquetFilePath(
        string parquetRoot,
        string tenantId,
        string deviceId,
        DateTimeOffset bucketStart)
    {
        var directory = BuildBucketDirectory(parquetRoot, tenantId, deviceId, bucketStart);
        return Path.Combine(directory, $"telemetry_{bucketStart:yyyyMMdd_HHmm}.parquet");
    }

    public static string BuildIndexFilePath(
        string indexRoot,
        string tenantId,
        string deviceId,
        DateTimeOffset bucketStart)
    {
        var directory = BuildBucketDirectory(indexRoot, tenantId, deviceId, bucketStart);
        return Path.Combine(directory, $"telemetry_{bucketStart:yyyyMMdd_HHmm}.json");
    }

    public static string BuildBucketDirectory(
        string root,
        string tenantId,
        string deviceId,
        DateTimeOffset bucketStart)
    {
        return Path.Combine(
            root,
            $"tenant={tenantId}",
            $"device={deviceId}",
            $"date={bucketStart:yyyy-MM-dd}",
            $"hour={bucketStart:HH}");
    }

    public static bool TryParseStageFile(string stageRoot, string filePath, out TelemetryBucket bucket)
    {
        bucket = default;
        if (!filePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = Path.GetRelativePath(stageRoot, filePath);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length < 5)
        {
            return false;
        }

        if (!segments[0].StartsWith("tenant=", StringComparison.OrdinalIgnoreCase)
            || !segments[1].StartsWith("device=", StringComparison.OrdinalIgnoreCase)
            || !segments[2].StartsWith("date=", StringComparison.OrdinalIgnoreCase)
            || !segments[3].StartsWith("hour=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var tenantId = segments[0]["tenant=".Length..];
        var deviceId = segments[1]["device=".Length..];
        var date = segments[2]["date=".Length..];
        var fileName = segments[4];
        var timestampPart = Path.GetFileNameWithoutExtension(fileName).Replace("telemetry_", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (!DateTimeOffset.TryParseExact(
                timestampPart,
                "yyyyMMdd_HHmm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var bucketStart))
        {
            return false;
        }

        bucket = new TelemetryBucket(tenantId, deviceId, bucketStart);
        return true;
    }
}

public readonly record struct TelemetryBucket(string TenantId, string DeviceId, DateTimeOffset BucketStart);
