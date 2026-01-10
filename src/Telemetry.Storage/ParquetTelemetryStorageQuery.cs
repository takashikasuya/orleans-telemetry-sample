using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Parquet;
using Parquet.Data;

namespace Telemetry.Storage;

public sealed class ParquetTelemetryStorageQuery : ITelemetryStorageQuery
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly TelemetryStorageOptions _options;

    public ParquetTelemetryStorageQuery(IOptions<TelemetryStorageOptions> options)
    {
        _options = options.Value;
    }

    public async Task<IReadOnlyList<TelemetryQueryResult>> QueryAsync(TelemetryQueryRequest request, CancellationToken ct)
    {
        var results = new List<TelemetryQueryResult>();
        var bucketMinutes = Math.Max(1, _options.BucketMinutes);
        var startBucket = TelemetryStoragePaths.GetBucketStart(request.From, bucketMinutes);
        var endBucket = TelemetryStoragePaths.GetBucketStart(request.To, bucketMinutes);
        var limit = request.Limit ?? _options.DefaultQueryLimit;

        for (var bucket = startBucket; bucket <= endBucket; bucket = bucket.AddMinutes(bucketMinutes))
        {
            ct.ThrowIfCancellationRequested();
            var indexPath = TelemetryStoragePaths.BuildIndexFilePath(
                _options.IndexPath,
                request.TenantId,
                request.DeviceId,
                bucket);
            if (!File.Exists(indexPath))
            {
                continue;
            }

            var entry = await ReadIndexAsync(indexPath, ct);
            if (entry is null)
            {
                continue;
            }

            if (entry.MaxOccurredAt < request.From || entry.MinOccurredAt > request.To)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(request.PointId)
                && entry.PointIds.Count > 0
                && !entry.PointIds.Contains(request.PointId, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var parquetPath = TelemetryStoragePaths.BuildParquetFilePath(
                _options.ParquetPath,
                request.TenantId,
                request.DeviceId,
                bucket);
            if (!File.Exists(parquetPath))
            {
                continue;
            }

            await ReadParquetAsync(parquetPath, request, results, limit, ct);
            if (results.Count >= limit)
            {
                break;
            }
        }

        return results;
    }

    private static async Task<TelemetryIndexEntry?> ReadIndexAsync(string indexPath, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(indexPath, ct);
        return JsonSerializer.Deserialize<TelemetryIndexEntry>(json, SerializerOptions);
    }

    private static async Task ReadParquetAsync(
        string parquetPath,
        TelemetryQueryRequest request,
        List<TelemetryQueryResult> results,
        int limit,
        CancellationToken ct)
    {
        await using var fileStream = new FileStream(parquetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var parquetReader = await ParquetReader.CreateAsync(fileStream, cancellationToken: ct);
        var schema = parquetReader.Schema;
        var fieldMap = schema.Fields.ToDictionary(f => f.Name, f => (DataField)f, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < parquetReader.RowGroupCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var groupReader = parquetReader.OpenRowGroupReader(i);
            var tenantColumn = await groupReader.ReadColumnAsync(fieldMap["tenantId"], ct);
            var deviceColumn = await groupReader.ReadColumnAsync(fieldMap["deviceId"], ct);
            var pointColumn = await groupReader.ReadColumnAsync(fieldMap["pointId"], ct);
            var occurredColumn = await groupReader.ReadColumnAsync(fieldMap["occurredAt"], ct);
            var sequenceColumn = await groupReader.ReadColumnAsync(fieldMap["sequence"], ct);
            var valueColumn = await groupReader.ReadColumnAsync(fieldMap["valueJson"], ct);
            var payloadColumn = await groupReader.ReadColumnAsync(fieldMap["payloadJson"], ct);
            var tagsColumn = await groupReader.ReadColumnAsync(fieldMap["tagsJson"], ct);

            var tenants = (string[])tenantColumn.Data;
            var devices = (string[])deviceColumn.Data;
            var points = (string[])pointColumn.Data;
            var occurred = (DateTime[])occurredColumn.Data;
            var sequences = (long[])sequenceColumn.Data;
            var values = (string?[])valueColumn.Data;
            var payloads = (string?[])payloadColumn.Data;
            var tags = (string?[])tagsColumn.Data;

            for (var row = 0; row < tenants.Length; row++)
            {
                if (!string.Equals(tenants[row], request.TenantId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.Equals(devices[row], request.DeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(request.PointId)
                    && !string.Equals(points[row], request.PointId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var occurredAt = new DateTimeOffset(DateTime.SpecifyKind(occurred[row], DateTimeKind.Utc));
                if (occurredAt < request.From || occurredAt > request.To)
                {
                    continue;
                }

                var tagsMap = string.IsNullOrWhiteSpace(tags[row])
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(tags[row]!, SerializerOptions);

                results.Add(new TelemetryQueryResult(
                    tenants[row],
                    devices[row],
                    points[row],
                    occurredAt,
                    sequences[row],
                    values[row],
                    payloads[row],
                    tagsMap));

                if (results.Count >= limit)
                {
                    return;
                }
            }
        }
    }
}
