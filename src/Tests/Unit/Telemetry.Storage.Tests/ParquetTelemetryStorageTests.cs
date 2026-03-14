using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telemetry.Ingest;
using Telemetry.Storage;
using Xunit;

namespace Telemetry.Storage.Tests;

public sealed class ParquetTelemetryStorageTests
{
    [Fact]
    public async Task WriteBatchAsync_WritesStageFile()
    {
        var root = CreateTempDirectory();
        var options = Options.Create(new TelemetryStorageOptions
        {
            StagePath = root,
            BucketMinutes = 15
        });
        var sink = new ParquetTelemetryEventSink(options, NullLogger<ParquetTelemetryEventSink>.Instance);

        var timestamp = new DateTimeOffset(2024, 1, 1, 10, 7, 0, TimeSpan.Zero);
        var envelope = CreateEnvelope(timestamp, "tenant-a", "device-1", "point-1", 1, 42.5);

        await sink.WriteBatchAsync(new[] { envelope }, CancellationToken.None);

        var bucketStart = TelemetryStoragePaths.GetBucketStart(timestamp, 15);
        var stageFile = TelemetryStoragePaths.BuildStageFilePath(root, "tenant-a", "device-1", bucketStart);
        File.Exists(stageFile).Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(stageFile);
        lines.Should().HaveCount(1);
        lines[0].Should().Contain("tenant-a");
    }

    [Fact]
    public async Task Compactor_WritesParquetAndIndex()
    {
        var stageRoot = CreateTempDirectory();
        var parquetRoot = CreateTempDirectory();
        var indexRoot = CreateTempDirectory();
        var options = Options.Create(new TelemetryStorageOptions
        {
            StagePath = stageRoot,
            ParquetPath = parquetRoot,
            IndexPath = indexRoot,
            BucketMinutes = 15
        });
        var sink = new ParquetTelemetryEventSink(options, NullLogger<ParquetTelemetryEventSink>.Instance);
        var compactor = new TelemetryStorageCompactor(options, NullLogger<TelemetryStorageCompactor>.Instance);

        var timestamp = new DateTimeOffset(2024, 2, 1, 12, 3, 0, TimeSpan.Zero);
        var envelopes = new[]
        {
            CreateEnvelope(timestamp, "tenant-a", "device-1", "point-1", 1, 10),
            CreateEnvelope(timestamp.AddMinutes(1), "tenant-a", "device-1", "point-2", 2, 11)
        };
        await sink.WriteBatchAsync(envelopes, CancellationToken.None);

        var count = await compactor.CompactAsync(CancellationToken.None);
        count.Should().Be(1);

        var bucketStart = TelemetryStoragePaths.GetBucketStart(timestamp, 15);
        var stageFile = TelemetryStoragePaths.BuildStageFilePath(stageRoot, "tenant-a", "device-1", bucketStart);
        var parquetFile = TelemetryStoragePaths.BuildParquetFilePath(parquetRoot, "tenant-a", "device-1", bucketStart);
        var indexFile = TelemetryStoragePaths.BuildIndexFilePath(indexRoot, "tenant-a", "device-1", bucketStart);

        File.Exists(stageFile).Should().BeFalse();
        File.Exists(parquetFile).Should().BeTrue();
        File.Exists(indexFile).Should().BeTrue();

        var indexJson = await File.ReadAllTextAsync(indexFile);
        var entry = JsonSerializer.Deserialize<TelemetryIndexEntry>(indexJson);
        entry.Should().NotBeNull();
        entry!.RecordCount.Should().Be(2);
    }

    [Fact]
    public async Task QueryAsync_ReturnsFilteredTelemetry()
    {
        var stageRoot = CreateTempDirectory();
        var parquetRoot = CreateTempDirectory();
        var indexRoot = CreateTempDirectory();
        var options = Options.Create(new TelemetryStorageOptions
        {
            StagePath = stageRoot,
            ParquetPath = parquetRoot,
            IndexPath = indexRoot,
            BucketMinutes = 15,
            DefaultQueryLimit = 10
        });
        var sink = new ParquetTelemetryEventSink(options, NullLogger<ParquetTelemetryEventSink>.Instance);
        var compactor = new TelemetryStorageCompactor(options, NullLogger<TelemetryStorageCompactor>.Instance);
        var query = new ParquetTelemetryStorageQuery(options);

        var timestamp = new DateTimeOffset(2024, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var envelopes = new[]
        {
            CreateEnvelope(timestamp, "tenant-a", "device-1", "point-1", 1, 100),
            CreateEnvelope(timestamp.AddMinutes(2), "tenant-a", "device-1", "point-2", 2, 101)
        };
        await sink.WriteBatchAsync(envelopes, CancellationToken.None);
        await compactor.CompactAsync(CancellationToken.None);

        var results = await query.QueryAsync(
            new TelemetryQueryRequest(
                "tenant-a",
                "device-1",
                timestamp.AddMinutes(-1),
                timestamp.AddMinutes(5),
                "point-2",
                null),
            CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].PointId.Should().Be("point-2");
        results[0].ValueJson.Should().Be("101");
    }

    [Fact]
    public async Task QueryAsync_ReturnsPayloadAndTagsFromParquet()
    {
        var stageRoot = CreateTempDirectory();
        var parquetRoot = CreateTempDirectory();
        var indexRoot = CreateTempDirectory();
        var options = Options.Create(new TelemetryStorageOptions
        {
            StagePath = stageRoot,
            ParquetPath = parquetRoot,
            IndexPath = indexRoot,
            BucketMinutes = 15,
            DefaultQueryLimit = 10
        });
        var sink = new ParquetTelemetryEventSink(options, NullLogger<ParquetTelemetryEventSink>.Instance);
        var compactor = new TelemetryStorageCompactor(options, NullLogger<TelemetryStorageCompactor>.Instance);
        var query = new ParquetTelemetryStorageQuery(options);

        var timestamp = new DateTimeOffset(2024, 4, 1, 8, 0, 0, TimeSpan.Zero);
        using var payload = JsonDocument.Parse("{\"note\":\"ok\"}");
        var tags = new Dictionary<string, string> { { "source", "unit-test" } };
        var envelope = CreateEnvelope(timestamp, "tenant-a", "device-1", "point-1", 1, 123, payload, tags);

        await sink.WriteBatchAsync(new[] { envelope }, CancellationToken.None);
        await compactor.CompactAsync(CancellationToken.None);

        var results = await query.QueryAsync(
            new TelemetryQueryRequest(
                "tenant-a",
                "device-1",
                timestamp.AddMinutes(-1),
                timestamp.AddMinutes(1),
                "point-1",
                null),
            CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].PayloadJson.Should().Be("{\"note\":\"ok\"}");
        results[0].Tags.Should().NotBeNull();
        results[0].Tags!["source"].Should().Be("unit-test");
    }

    private static TelemetryEventEnvelope CreateEnvelope(
        DateTimeOffset timestamp,
        string tenantId,
        string deviceId,
        string pointId,
        long sequence,
        object value,
        JsonDocument? payload = null,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        return new TelemetryEventEnvelope(
            tenantId,
            "Building-A",
            "Space-A",
            deviceId,
            pointId,
            sequence,
            timestamp,
            timestamp,
            TelemetryEventType.Telemetry,
            null,
            value,
            payload,
            tags);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "telemetry-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
