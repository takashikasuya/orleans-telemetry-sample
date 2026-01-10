using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telemetry.Ingest;

namespace Telemetry.Storage;

public sealed class ParquetTelemetryEventSink : ITelemetryEventSink
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly TelemetryStorageOptions _options;
    private readonly ILogger<ParquetTelemetryEventSink> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();

    public ParquetTelemetryEventSink(
        IOptions<TelemetryStorageOptions> options,
        ILogger<ParquetTelemetryEventSink> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "ParquetStorage";

    public Task WriteAsync(TelemetryEventEnvelope envelope, CancellationToken ct)
    {
        return WriteBatchAsync(new[] { envelope }, ct);
    }

    public async Task WriteBatchAsync(IReadOnlyList<TelemetryEventEnvelope> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var bucketMinutes = Math.Max(1, _options.BucketMinutes);
        var groups = batch.GroupBy(envelope => new
        {
            envelope.TenantId,
            envelope.DeviceId,
            BucketStart = TelemetryStoragePaths.GetBucketStart(envelope.IngestedAt, bucketMinutes)
        });

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var filePath = TelemetryStoragePaths.BuildStageFilePath(
                _options.StagePath,
                group.Key.TenantId,
                group.Key.DeviceId,
                group.Key.BucketStart);

            var fileLock = _fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
            await fileLock.WaitAsync(ct);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                await using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                await using var writer = new StreamWriter(stream);
                foreach (var envelope in group)
                {
                    var record = TelemetryStageRecord.FromEnvelope(envelope);
                    var json = JsonSerializer.Serialize(record, SerializerOptions);
                    await writer.WriteLineAsync(json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write telemetry stage file {FilePath}.", filePath);
                throw;
            }
            finally
            {
                fileLock.Release();
            }
        }
    }
}
