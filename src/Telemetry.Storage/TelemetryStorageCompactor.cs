using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Parquet;
using Parquet.Data;

namespace Telemetry.Storage;

public sealed class TelemetryStorageCompactor
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly TelemetryStorageOptions _options;
    private readonly ILogger<TelemetryStorageCompactor> _logger;

    public TelemetryStorageCompactor(
        IOptions<TelemetryStorageOptions> options,
        ILogger<TelemetryStorageCompactor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> CompactAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_options.StagePath))
        {
            return 0;
        }

        var count = 0;
        foreach (var filePath in Directory.EnumerateFiles(_options.StagePath, "*.jsonl", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (!TelemetryStoragePaths.TryParseStageFile(_options.StagePath, filePath, out var bucket))
            {
                continue;
            }

            await CompactStageFileAsync(filePath, bucket, ct);
            count++;
        }

        return count;
    }

    public async Task CompactStageFileAsync(string stageFilePath, TelemetryBucket bucket, CancellationToken ct)
    {
        var records = new List<TelemetryStageRecord>();
        await using (var stream = new FileStream(stageFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new StreamReader(stream))
        {
            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var record = JsonSerializer.Deserialize<TelemetryStageRecord>(line, SerializerOptions);
                if (record is not null)
                {
                    records.Add(record);
                }
            }
        }

        if (records.Count == 0)
        {
            File.Delete(stageFilePath);
            return;
        }

        var parquetPath = TelemetryStoragePaths.BuildParquetFilePath(
            _options.ParquetPath,
            bucket.TenantId,
            bucket.DeviceId,
            bucket.BucketStart);
        Directory.CreateDirectory(Path.GetDirectoryName(parquetPath)!);

        await WriteParquetAsync(parquetPath, records, ct);
        await WriteIndexAsync(bucket, parquetPath, records, ct);

        File.Delete(stageFilePath);
    }

    private static async Task WriteParquetAsync(string parquetPath, List<TelemetryStageRecord> records, CancellationToken ct)
    {
        var schema = new Schema(
            new DataField<string>("tenantId"),
            new DataField<string>("buildingName"),
            new DataField<string>("spaceId"),
            new DataField<string>("deviceId"),
            new DataField<string>("pointId"),
            new DataField<long>("sequence"),
            new DateTimeDataField("occurredAt", DateTimeFormat.DateAndTime),
            new DateTimeDataField("ingestedAt", DateTimeFormat.DateAndTime),
            new DataField<int>("eventType"),
            new DataField<int>("severity", hasNulls: true),
            new DataField<string>("valueJson", hasNulls: true),
            new DataField<string>("payloadJson", hasNulls: true),
            new DataField<string>("tagsJson", hasNulls: true));

        await using var fileStream = new FileStream(parquetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var parquetWriter = await ParquetWriter.CreateAsync(schema, fileStream, cancellationToken: ct);
        using var rowGroupWriter = parquetWriter.CreateRowGroup();

        await rowGroupWriter.WriteColumnAsync(new DataColumn(
            (DataField)schema.Fields[0],
            records.Select(r => r.TenantId).ToArray()), ct);
        await rowGroupWriter.WriteColumnAsync(new DataColumn(
            (DataField)schema.Fields[1],
            records.Select(r => r.BuildingName).ToArray()), ct);
        await rowGroupWriter.WriteColumnAsync(new DataColumn(
            (DataField)schema.Fields[2],
            records.Select(r => r.SpaceId).ToArray()), ct);
        await rowGroupWriter.WriteColumnAsync(new DataColumn(
            (DataField)schema.Fields[3],
            records.Select(r => r.DeviceId).ToArray()), ct);
        await rowGroupWriter.WriteColumnAsync(new DataColumn(
            (DataField)schema.Fields[4],
            records.Select(r => r.PointId).ToArray()), ct);
        await rowGroupWriter.WriteColumnAsync(new DataColumn(
            (DataField)schema.Fields[5],
            records.Select(r => r.Sequence).ToArray()), ct);
        await rowGroupWriter.WriteColumnAsync(new DataColumn(
            (DataField)schema.Fields[6],
            records.Select(r => r.OccurredAt.UtcDateTime).ToArray()), ct);
        await rowGroupWriter.WriteColumnAsync(new DataColumn(
            (DataField)schema.Fields[7],
            records.Select(r => r.IngestedAt.UtcDateTime).ToArray()), ct);
        await rowGroupWriter.WriteColumnAsync(new DataColumn(
            (DataField)schema.Fields[8],
            records.Select(r => (int)r.EventType).ToArray()), ct);
        await rowGroupWriter.WriteColumnAsync(new DataColumn(
            (DataField)schema.Fields[9],
            records.Select(r => r.Severity.HasValue ? (int?)r.Severity.Value : null).ToArray()), ct);
        await rowGroupWriter.WriteColumnAsync(new DataColumn(
            (DataField)schema.Fields[10],
            records.Select(r => r.ValueJson).ToArray()), ct);
        await rowGroupWriter.WriteColumnAsync(new DataColumn(
            (DataField)schema.Fields[11],
            records.Select(r => r.PayloadJson).ToArray()), ct);
        await rowGroupWriter.WriteColumnAsync(new DataColumn(
            (DataField)schema.Fields[12],
            records.Select(r => r.Tags is null ? null : JsonSerializer.Serialize(r.Tags)).ToArray()), ct);
    }

    private async Task WriteIndexAsync(TelemetryBucket bucket, string parquetPath, List<TelemetryStageRecord> records, CancellationToken ct)
    {
        var min = records.Min(r => r.OccurredAt);
        var max = records.Max(r => r.OccurredAt);
        var pointIds = records.Select(r => r.PointId).Distinct().OrderBy(id => id).ToArray();
        var entry = new TelemetryIndexEntry(
            bucket.TenantId,
            bucket.DeviceId,
            bucket.BucketStart,
            min,
            max,
            records.Count,
            pointIds,
            Path.GetFileName(parquetPath));

        var indexPath = TelemetryStoragePaths.BuildIndexFilePath(
            _options.IndexPath,
            bucket.TenantId,
            bucket.DeviceId,
            bucket.BucketStart);
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);

        var json = JsonSerializer.Serialize(entry, SerializerOptions);
        await File.WriteAllTextAsync(indexPath, json, ct);
    }
}
